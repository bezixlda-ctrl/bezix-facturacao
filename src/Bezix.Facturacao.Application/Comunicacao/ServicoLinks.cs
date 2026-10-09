using System.Security.Cryptography;
using System.Text;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Documentos;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Comunicacao;

public sealed record LinkAberto(LinkPartilha Link, Documento Documento, Cliente Cliente);

/// <summary>
/// Links seguros para o cliente ver um documento sem login: token aleatório de 256 bits, guardado só como hash,
/// com validade. A página pública mostra o documento, o PDF, as contas bancárias e permite comunicar o pagamento.
/// </summary>
public sealed class ServicoLinks(
    IAppDbContext db,
    ServicoEmpresa empresas,
    IEnderecoPublico endereco,
    IArmazenamentoFicheiros ficheiros,
    IServicoAuditoria auditoria,
    TimeProvider relogio)
{
    public const long TamanhoMaximoComprovativo = 5 * 1024 * 1024;

    /// <summary>Cria um link novo para o documento e devolve o URL completo (ou só o caminho, se não houver endereço público).</summary>
    public async Task<string> CriarAsync(int documentoId, CancellationToken ct = default)
    {
        var doc = await db.Documentos.AsNoTracking().SingleAsync(d => d.Id == documentoId, ct);
        var empresa = await empresas.ObterAsync(ct);
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        db.LinksPartilha.Add(new LinkPartilha
        {
            DocumentoId = doc.Id,
            ClienteId = doc.ClienteId,
            TokenHash = Hash(token),
            ExpiraEm = relogio.GetUtcNow().UtcDateTime.AddDays(Math.Max(1, empresa.DiasValidadeLink)),
        });
        await db.SaveChangesAsync(ct);
        var baseUrl = (empresa.UrlPublica ?? endereco.Base ?? "").TrimEnd('/');
        return $"{baseUrl}/p/{token}";
    }

    /// <summary>Abre um link: null se não existir, tiver expirado ou sido revogado, ou se o documento não estiver emitido.</summary>
    public async Task<LinkAberto?> AbrirAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 20 or > 100) return null;
        var hash = Hash(token);
        var link = await db.LinksPartilha.SingleOrDefaultAsync(l => l.TokenHash == hash, ct);
        if (link is null || link.RevogadoEm is not null || link.ExpiraEm < relogio.GetUtcNow().UtcDateTime) return null;

        var doc = await db.Documentos.AsNoTracking()
            .Include(d => d.Linhas.OrderBy(l => l.Ordem)).Include(d => d.IvaResumo)
            .SingleOrDefaultAsync(d => d.Id == link.DocumentoId, ct);
        if (doc is null || !doc.Emitido) return null;
        var cliente = await db.Clientes.AsNoTracking().SingleAsync(c => c.Id == link.ClienteId, ct);
        return new LinkAberto(link, doc, cliente);
    }

    /// <summary>Conta a visita; a primeira fica na auditoria ("visto pelo cliente").</summary>
    public async Task RegistarVisitaAsync(LinkPartilha link, string? ip, CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var primeira = link.PrimeiraVisita is null;
        link.PrimeiraVisita ??= agora;
        link.UltimaVisita = agora;
        link.Visitas++;
        if (primeira)
        {
            var numero = await db.Documentos.AsNoTracking().Where(d => d.Id == link.DocumentoId).Select(d => d.NumeroFormatado).SingleAsync(ct);
            auditoria.Registar(nameof(Documento), link.DocumentoId.ToString(), "Documento.Visto", null, new { NumeroFormatado = numero, ip }, utilizador: "cliente");
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Primeira visita do cliente a algum link deste documento (para mostrar "Visto em …").</summary>
    public Task<DateTime?> VistoEmAsync(int documentoId, CancellationToken ct = default) =>
        db.LinksPartilha.AsNoTracking().Where(l => l.DocumentoId == documentoId && l.PrimeiraVisita != null)
            .MinAsync(l => l.PrimeiraVisita, ct);

    public async Task<Resultado> ComunicarPagamentoAsync(LinkAberto aberto, decimal valor, DateOnly data, string? referencia, string? mensagem,
        byte[]? comprovativo, string? ip, CancellationToken ct = default)
    {
        var erros = new List<string>();
        var hoje = DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime.AddHours(2));
        if (valor <= 0 || decimal.Round(valor, 2) != valor) erros.Add("Indique o valor pago (até 2 casas decimais).");
        if (data > hoje) erros.Add("A data do pagamento não pode ser futura.");
        if (data < hoje.AddYears(-1)) erros.Add("A data do pagamento é demasiado antiga.");
        string? tipo = null, extensao = null;
        if (comprovativo is { Length: > 0 })
        {
            if (comprovativo.Length > TamanhoMaximoComprovativo) erros.Add("O comprovativo tem mais de 5 MB.");
            (tipo, extensao) = TipoComprovativo(comprovativo);
            if (tipo is null) erros.Add("O comprovativo tem de ser um PDF, JPG ou PNG.");
        }
        else erros.Add("Anexe o comprovativo do pagamento (PDF, JPG ou PNG).");
        if (await db.PagamentosComunicados.CountAsync(p => p.ClienteId == aberto.Cliente.Id && p.Estado == EstadoPagamentoComunicado.PorValidar, ct) >= 10)
            erros.Add("Já há vários pagamentos à espera de validação. Aguarde o contacto da nossa equipa.");
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        var caminho = await ficheiros.GuardarAsync("comprovativos", extensao!, comprovativo!, ct);
        var p = new PagamentoComunicado
        {
            ClienteId = aberto.Cliente.Id,
            DocumentoId = aberto.Documento.Id,
            Valor = valor,
            Data = data,
            Referencia = Limpar(referencia, 100),
            Mensagem = Limpar(mensagem, 1000),
            ComprovativoPath = caminho,
            ComprovativoTipo = tipo,
            Ip = ip,
            CreatedBy = "cliente",
        };
        db.PagamentosComunicados.Add(p);
        auditoria.Registar(nameof(PagamentoComunicado), null, "Pagamento.Comunicado", null,
            new { Cliente = aberto.Cliente.Nome, aberto.Documento.NumeroFormatado, valor, data, referencia }, utilizador: "cliente");
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public static (string? Tipo, string? Extensao) TipoComprovativo(byte[] b) =>
        b.AsSpan().StartsWith("%PDF"u8) ? ("application/pdf", ".pdf")
        : b.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) ? ("image/png", ".png")
        : b.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) ? ("image/jpeg", ".jpg")
        : (null, null);

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Limpar(string? v, int max) =>
        string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length > max ? v.Trim()[..max] : v.Trim();
}
