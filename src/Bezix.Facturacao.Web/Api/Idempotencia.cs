using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bezix.Facturacao.Domain.Integracoes;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Web.Api;

/// <summary>
/// Cabeçalho Idempotency-Key obrigatório nos POST que criam documentos e recibos: repetir o pedido (ex.: depois de
/// uma falha de rede) devolve a mesma resposta em vez de duplicar a factura. Mesma chave com outro corpo → 422.
/// </summary>
public sealed class Idempotencia(AppDbContext db) : IEndpointFilter
{
    public const string Cabecalho = "Idempotency-Key";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext contexto, EndpointFilterDelegate seguinte)
    {
        var http = contexto.HttpContext;
        var chave = http.Request.Headers[Cabecalho].ToString().Trim();
        if (chave.Length is 0 or > 100)
            return Results.Problem(statusCode: 400, title: "Falta o cabeçalho Idempotency-Key",
                detail: "Envie um identificador único por operação (ex.: um GUID) no cabeçalho Idempotency-Key, até 100 caracteres.");

        var chaveApiId = int.Parse(http.User.FindFirst(AutenticacaoApi.ClaimChaveId)!.Value);
        var rota = $"{http.Request.Method} {http.Request.Path}";
        var corpo = contexto.Arguments.FirstOrDefault(a => a is not null && a.GetType().Name.EndsWith("Api", StringComparison.Ordinal));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rota + "\n" + JsonSerializer.Serialize(corpo, Json))));

        var existente = await db.PedidosIdempotentes.AsNoTracking().SingleOrDefaultAsync(p => p.ChaveApiId == chaveApiId && p.Chave == chave);
        if (existente is not null) return Repetir(existente, hash);

        var pedido = new PedidoIdempotente { ChaveApiId = chaveApiId, Chave = chave, Rota = rota, HashCorpo = hash, CreatedAt = DateTime.UtcNow };
        db.PedidosIdempotentes.Add(pedido);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // Outro pedido com a mesma chave chegou ao mesmo tempo.
            db.ChangeTracker.Clear();
            var outro = await db.PedidosIdempotentes.AsNoTracking().SingleAsync(p => p.ChaveApiId == chaveApiId && p.Chave == chave);
            return Repetir(outro, hash);
        }
        var id = pedido.Id;

        object? resultado;
        try { resultado = await seguinte(contexto); }
        catch
        {
            await db.PedidosIdempotentes.Where(p => p.Id == id).ExecuteDeleteAsync();
            throw;
        }

        // Results<A, B> embrulha o resultado real.
        var real = resultado;
        while (real is INestedHttpResult aninhado) real = aninhado.Result;
        var (codigo, valor) = real switch
        {
            IStatusCodeHttpResult s and IValueHttpResult v => (s.StatusCode ?? 200, v.Value),
            IStatusCodeHttpResult s => (s.StatusCode ?? 200, (object?)null),
            _ => (200, real),
        };
        if (codigo >= 500)
        {
            await db.PedidosIdempotentes.Where(p => p.Id == id).ExecuteDeleteAsync();
            return resultado;
        }
        var texto = valor is null ? null : JsonSerializer.Serialize(valor, valor.GetType(), Json);
        await db.PedidosIdempotentes.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CodigoResposta, codigo).SetProperty(p => p.Resposta, texto));
        return resultado;
    }

    private static IResult Repetir(PedidoIdempotente p, string hash)
    {
        if (p.HashCorpo != hash)
            return Results.Problem(statusCode: 422, title: "Idempotency-Key já usada",
                detail: "Esta Idempotency-Key já foi usada com outro pedido. Use uma chave nova para uma operação nova.");
        if (p.CodigoResposta is null)
            return Results.Problem(statusCode: 409, title: "Pedido em curso", detail: "Um pedido com esta Idempotency-Key ainda está a ser processado.");
        return Results.Content(p.Resposta ?? "", p.CodigoResposta >= 400 ? "application/problem+json" : "application/json", Encoding.UTF8, p.CodigoResposta);
    }
}
