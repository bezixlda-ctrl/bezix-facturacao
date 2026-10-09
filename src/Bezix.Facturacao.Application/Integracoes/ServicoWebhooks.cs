using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Integracoes;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Integracoes;

/// <summary>
/// Webhooks: cada evento gera uma entrega por subscrição. A entrega é um POST JSON assinado
/// (X-Signature: sha256=HMAC de "X-Timestamp.corpo" com o segredo da subscrição; X-Timestamp) e repete-se com espera crescente
/// (1, 5, 30 min, 2 h, 12 h). Uma subscrição é desactivada depois de 5 entregas seguidas falhadas.
/// </summary>
public sealed class ServicoWebhooks(
    IAppDbContext db,
    IProtecaoSegredos protecao,
    IEnviadorWebhook enviador,
    ISinalFilas sinal,
    IServicoAuditoria auditoria,
    TimeProvider relogio) : IPublicadorEventos
{
    public static readonly TimeSpan[] Esperas =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12)];
    public const int FalhasParaDesactivar = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private List<WebhookSubscricao>? _activas;

    // ------------------------------------------------------------------ Publicação

    /// <summary>
    /// Acrescenta as entregas ao contexto (são gravadas com o SaveChanges de quem publica, na mesma transacção)
    /// e acorda o processador. Se o SaveChanges falhar, o evento também não sai.
    /// </summary>
    public void Publicar(string evento, object dados)
    {
        _activas ??= db.WebhookSubscricoes.AsNoTracking().Where(s => s.Activa).ToList();
        var destinos = _activas.Where(s => s.Subscreve(evento)).ToList();
        if (destinos.Count == 0) return;

        var agora = relogio.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { id, evento, ocorridoEm = agora, dados }, Json);
        foreach (var s in destinos)
            db.WebhookEntregas.Add(new WebhookEntrega
            {
                SubscricaoId = s.Id, EventoId = id, Evento = evento, Payload = payload, CreatedAt = agora, ProximaTentativa = agora,
            });
        sinal.Sinalizar();
    }

    // ------------------------------------------------------------------ Entrega

    public async Task<int> EntregarPendentesAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var ids = await db.WebhookEntregas.AsNoTracking()
            .Where(e => e.Estado == EstadoEntrega.Pendente && (e.ProximaTentativa == null || e.ProximaTentativa <= agora) && e.Subscricao!.Activa)
            .OrderBy(e => e.Id).Take(50).Select(e => e.Id).ToListAsync(ct);
        foreach (var id in ids) await EntregarAsync(id, ct);
        return ids.Count;
    }

    private async Task EntregarAsync(long id, CancellationToken ct)
    {
        var e = await db.WebhookEntregas.Include(x => x.Subscricao).SingleAsync(x => x.Id == id, ct);
        if (e.Estado != EstadoEntrega.Pendente || e.Subscricao is not { Activa: true } s) return;

        var segredo = protecao.Decifrar(s.SegredoCifrado);
        var timestamp = relogio.GetUtcNow().ToUnixTimeSeconds().ToString();
        var cabecalhos = new Dictionary<string, string>
        {
            ["X-Signature"] = "sha256=" + Assinar(segredo, timestamp + "." + e.Payload),
            ["X-Timestamp"] = timestamp,
            ["X-Evento"] = e.Evento,
            ["X-Entrega-Id"] = e.EventoId.ToString(),
        };
        var r = await enviador.EnviarAsync(s.Url, e.Payload, cabecalhos, ct);
        e.Tentativas++;
        e.CodigoResposta = r.Codigo;
        e.Resposta = Truncar(r.Erro ?? r.Corpo, 1000);

        if (r.Sucesso)
        {
            e.Estado = EstadoEntrega.Entregue;
            e.EntregueEm = relogio.GetUtcNow().UtcDateTime;
            e.ProximaTentativa = null;
            s.FalhasSeguidas = 0;
        }
        else if (e.Tentativas > Esperas.Length)
        {
            e.Estado = EstadoEntrega.Falhou;
            e.ProximaTentativa = null;
            s.FalhasSeguidas++;
            if (s.FalhasSeguidas >= FalhasParaDesactivar)
            {
                s.Activa = false;
                s.DesactivadaEm = relogio.GetUtcNow().UtcDateTime;
                s.MotivoDesactivacao = $"{FalhasParaDesactivar} entregas seguidas falhadas. Última resposta: {(r.Codigo?.ToString() ?? r.Erro)}";
            }
        }
        else e.ProximaTentativa = relogio.GetUtcNow().UtcDateTime + Esperas[e.Tentativas - 1];
        await db.SaveChangesAsync(ct);
    }

    /// <summary>HMAC-SHA256 do corpo com o segredo, em hexadecimal minúsculo.</summary>
    public static string Assinar(string segredo, string corpo) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(corpo))).ToLowerInvariant();

    // ------------------------------------------------------------------ Gestão

    public Task<List<WebhookSubscricao>> SubscricoesAsync(CancellationToken ct = default) =>
        db.WebhookSubscricoes.AsNoTracking().OrderBy(s => s.Nome).ToListAsync(ct);

    /// <summary>Cria a subscrição e devolve o segredo (mostrado uma única vez).</summary>
    public async Task<(Resultado Resultado, string? Segredo)> CriarAsync(string nome, string url, IReadOnlyCollection<string> eventos, CancellationToken ct = default)
    {
        var erros = Validar(nome, url, eventos);
        if (erros.Count > 0) return (Resultado.Falha([.. erros]), null);
        var segredo = "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var s = new WebhookSubscricao
        {
            Nome = nome.Trim(), Url = url.Trim(), SegredoCifrado = protecao.Cifrar(segredo),
            Eventos = eventos.Contains("*") ? "*" : string.Join(' ', eventos.Distinct()),
        };
        db.WebhookSubscricoes.Add(s);
        await db.SaveChangesAsync(ct);
        auditoria.Registar(nameof(WebhookSubscricao), s.Id.ToString(), "Webhook.Criar", null, new { s.Nome, s.Url, s.Eventos });
        await db.SaveChangesAsync(ct);
        return (Resultado.Ok(), segredo);
    }

    public async Task<Resultado> ActualizarAsync(int id, string nome, string url, IReadOnlyCollection<string> eventos, bool activa, CancellationToken ct = default)
    {
        var erros = Validar(nome, url, eventos);
        if (erros.Count > 0) return Resultado.Falha([.. erros]);
        var s = await db.WebhookSubscricoes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Resultado.Falha("Subscrição não encontrada.");
        var antes = new { s.Nome, s.Url, s.Eventos, s.Activa };
        s.Nome = nome.Trim();
        s.Url = url.Trim();
        s.Eventos = eventos.Contains("*") ? "*" : string.Join(' ', eventos.Distinct());
        if (activa && !s.Activa) { s.FalhasSeguidas = 0; s.DesactivadaEm = null; s.MotivoDesactivacao = null; }
        s.Activa = activa;
        auditoria.Registar(nameof(WebhookSubscricao), id.ToString(), "Webhook.Alterar", antes, new { s.Nome, s.Url, s.Eventos, s.Activa });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public async Task<(Resultado Resultado, string? Segredo)> NovoSegredoAsync(int id, CancellationToken ct = default)
    {
        var s = await db.WebhookSubscricoes.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return (Resultado.Falha("Subscrição não encontrada."), null);
        var segredo = "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        s.SegredoCifrado = protecao.Cifrar(segredo);
        auditoria.Registar(nameof(WebhookSubscricao), id.ToString(), "Webhook.NovoSegredo");
        await db.SaveChangesAsync(ct);
        return (Resultado.Ok(), segredo);
    }

    public async Task<Resultado> TestarAsync(int id, CancellationToken ct = default)
    {
        var s = await db.WebhookSubscricoes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return Resultado.Falha("Subscrição não encontrada.");
        var agora = relogio.GetUtcNow().UtcDateTime;
        var eid = Guid.NewGuid();
        db.WebhookEntregas.Add(new WebhookEntrega
        {
            SubscricaoId = id, EventoId = eid, Evento = EventosWebhook.Teste, CreatedAt = agora, ProximaTentativa = agora,
            Payload = JsonSerializer.Serialize(new { id = eid, evento = EventosWebhook.Teste, ocorridoEm = agora, dados = new { mensagem = "Teste de ligação" } }, Json),
        });
        await db.SaveChangesAsync(ct);
        var entrega = await db.WebhookEntregas.AsNoTracking().Where(e => e.EventoId == eid).Select(e => e.Id).SingleAsync(ct);
        await EntregarAsync(entrega, ct);
        var r = await db.WebhookEntregas.AsNoTracking().SingleAsync(e => e.Id == entrega, ct);
        return r.Estado == EstadoEntrega.Entregue
            ? Resultado.Ok()
            : Resultado.Falha($"O destino respondeu {(r.CodigoResposta?.ToString() ?? "sem resposta")}: {r.Resposta}");
    }

    public async Task<Pagina<WebhookEntrega>> EntregasAsync(int? subscricaoId, EstadoEntrega? estado, int pagina, CancellationToken ct = default)
    {
        var (numero, tamanho) = Pagina<WebhookEntrega>.Normalizar(pagina, 50);
        var q = db.WebhookEntregas.AsNoTracking().Include(e => e.Subscricao).AsQueryable();
        if (subscricaoId is { } s) q = q.Where(e => e.SubscricaoId == s);
        if (estado is { } est) q = q.Where(e => e.Estado == est);
        var total = await q.CountAsync(ct);
        var itens = await q.OrderByDescending(e => e.Id).Skip((numero - 1) * tamanho).Take(tamanho).ToListAsync(ct);
        return new Pagina<WebhookEntrega>(itens, total, numero, tamanho);
    }

    public Task<int> ContarFalhadasAsync(CancellationToken ct = default) =>
        db.WebhookEntregas.AsNoTracking().CountAsync(e => e.Estado == EstadoEntrega.Falhou && e.CreatedAt >= DateTime.UtcNow.AddDays(-30), ct);

    /// <summary>Volta a pôr uma entrega na fila (botão "Reenviar") e tenta já.</summary>
    public async Task<Resultado> ReenviarAsync(long id, CancellationToken ct = default)
    {
        var e = await db.WebhookEntregas.Include(x => x.Subscricao).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return Resultado.Falha("Entrega não encontrada.");
        if (e.Subscricao is { Activa: false }) return Resultado.Falha("A subscrição está desactivada. Reactive-a primeiro.");
        e.Estado = EstadoEntrega.Pendente;
        e.Tentativas = 0;
        e.ProximaTentativa = relogio.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await EntregarAsync(id, ct);
        return Resultado.Ok();
    }

    private static List<string> Validar(string nome, string url, IReadOnlyCollection<string> eventos)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(nome)) erros.Add("Indique um nome.");
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
            erros.Add("O URL tem de ser um endereço https:// completo.");
        if (eventos.Count == 0) erros.Add("Escolha pelo menos um evento.");
        if (eventos.Any(e => e != "*" && !EventosWebhook.Todos.Contains(e))) erros.Add("Evento desconhecido.");
        return erros;
    }

    private static string? Truncar(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;
}
