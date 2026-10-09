using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Documentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bezix.Facturacao.Application.Comunicacao;

public sealed record EmailPreparado(string Para, string? Cc, string Assunto, string Corpo, bool AnexarPdf);

/// <summary>
/// Emails para clientes: modelos com variáveis, fila na base de dados e repetições.
/// Envia logo que é pedido; o que falhar é repetido pelo jobs/tick (1, 5, 30 min, 2 h, 12 h).
/// </summary>
public sealed partial class ServicoComunicacao(
    IAppDbContext db,
    IEnviadorEmail enviador,
    IArmazenamentoFicheiros ficheiros,
    ServicoEmpresa empresas,
    ServicoPdfDocumentos pdfs,
    IUtilizadorActual utilizador,
    IServicoAuditoria auditoria,
    TimeProvider relogio,
    ILogger<ServicoComunicacao> log)
{
    public static readonly TimeSpan[] Esperas =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12)];

    public static string CodigoModelo(TipoDocumento t) => $"documento.{t}";

    /// <summary>Email proposto para um documento, a partir do modelo do tipo de documento.</summary>
    public async Task<EmailPreparado?> PrepararDocumentoAsync(int documentoId, CancellationToken ct = default)
    {
        var doc = await db.Documentos.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentoId, ct);
        if (doc is null) return null;
        var cliente = await db.Clientes.AsNoTracking().SingleAsync(c => c.Id == doc.ClienteId, ct);
        var modelo = await db.ModelosComunicacao.AsNoTracking().SingleOrDefaultAsync(m => m.Codigo == CodigoModelo(doc.TipoDoc) && m.Activo, ct);
        var empresa = await empresas.ObterAsync(ct);
        var contas = await db.ContasBancarias.AsNoTracking().Where(c => c.MostrarNoDocumento).OrderBy(c => c.Ordem).ToListAsync(ct);

        var v = new Dictionary<string, string>
        {
            ["cliente.nome"] = cliente.Nome,
            ["cliente.contacto"] = cliente.Contacto ?? cliente.Nome,
            ["documento.tipo"] = TipoDocumentoInfo.Nome(doc.TipoDoc),
            ["documento.numero"] = doc.NumeroFormatado ?? "(rascunho)",
            ["documento.data"] = doc.Data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["documento.vencimento"] = doc.Vencimento?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            ["documento.total"] = Valor(doc.Total) + " " + doc.Moeda,
            ["empresa.nome"] = empresa.Nome,
            ["empresa.contas"] = string.Join("\n", contas.Select(c => $"{c.Banco}: {c.NumeroConta}" + (c.Nib is null ? "" : $" · NIB {c.Nib}"))),
            ["utilizador.nome"] = utilizador.Nome ?? "",
            ["link.documento"] = "",
        };

        var assunto = Substituir(modelo?.Assunto ?? "{documento.tipo} {documento.numero} · {empresa.nome}", v);
        var corpo = Substituir(modelo?.Corpo ?? "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo {documento.tipo} {documento.numero}.\n\nCumprimentos,\n{utilizador.nome}\n{empresa.nome}", v);
        var cc = string.Join(", ", new[] { modelo?.Cc, cliente.EmailCc }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new EmailPreparado(cliente.EmailEnvio ?? "", cc.Length == 0 ? null : cc, assunto, corpo, modelo?.AnexarPdf ?? true);
    }

    /// <summary>Põe o email na fila com o PDF do documento (o mesmo ficheiro da impressão) e tenta enviar já.</summary>
    public async Task<Resultado> EnviarDocumentoAsync(int documentoId, EmailPreparado email, CancellationToken ct = default)
    {
        var doc = await db.Documentos.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentoId, ct);
        if (doc is null) return Resultado.Falha("Documento não encontrado.");
        if (doc.Estado != EstadoDocumento.Emitido) return Resultado.Falha("Só documentos emitidos (e não anulados) podem ser enviados.");

        var erros = new List<string>();
        var destinatarios = Lista(email.Para);
        if (destinatarios.Count == 0) erros.Add("Indique o email do destinatário.");
        if (destinatarios.Concat(Lista(email.Cc)).Any(e => !Clientes.ServicoClientes.EmailValido(e))) erros.Add("Um dos emails não é válido.");
        if (string.IsNullOrWhiteSpace(email.Assunto)) erros.Add("Indique o assunto.");
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        string? anexos = null;
        if (email.AnexarPdf)
        {
            await pdfs.GarantirPdfGuardadoAsync(documentoId, ct);
            var caminho = await db.Documentos.AsNoTracking().Where(d => d.Id == documentoId).Select(d => d.PdfPath).SingleAsync(ct);
            anexos = $"{caminho}|{doc.NomeFicheiroPdf()}";
        }

        var item = new EmailFila
        {
            Para = string.Join(", ", destinatarios),
            Cc = Lista(email.Cc) is { Count: > 0 } cc ? string.Join(", ", cc) : null,
            Assunto = email.Assunto.Trim(),
            Corpo = email.Corpo,
            Anexos = anexos,
            ClienteId = doc.ClienteId,
            DocumentoId = doc.Id,
            ModeloCodigo = CodigoModelo(doc.TipoDoc),
            ProximaTentativa = relogio.GetUtcNow().UtcDateTime,
        };
        db.EmailsFila.Add(item);
        auditoria.Registar(nameof(Documento), doc.Id.ToString(), "Documento.Enviar", null, new { doc.NumeroFormatado, item.Para, item.Cc });
        await db.SaveChangesAsync(ct);

        await TentarEnviarAsync(item.Id, ct);
        var estado = await db.EmailsFila.AsNoTracking().Where(e => e.Id == item.Id).Select(e => new { e.Estado, e.ErroUltimo }).SingleAsync(ct);
        return estado.Estado == EstadoEmail.Enviado
            ? Resultado.Ok()
            : Resultado.Falha($"O email ficou na fila e vai ser repetido automaticamente. Erro: {estado.ErroUltimo}");
    }

    public Task<List<EmailFila>> HistoricoDocumentoAsync(int documentoId, CancellationToken ct = default) =>
        db.EmailsFila.AsNoTracking().Where(e => e.DocumentoId == documentoId).OrderByDescending(e => e.Id).ToListAsync(ct);

    /// <summary>Repetições pendentes (chamado pelo jobs/tick).</summary>
    public async Task<int> ProcessarPendentesAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var ids = await db.EmailsFila.AsNoTracking()
            .Where(e => e.Estado == EstadoEmail.Pendente && (e.ProximaTentativa == null || e.ProximaTentativa <= agora))
            .OrderBy(e => e.Id).Take(50).Select(e => e.Id).ToListAsync(ct);
        foreach (var id in ids) await TentarEnviarAsync(id, ct);
        return ids.Count;
    }

    private async Task TentarEnviarAsync(int id, CancellationToken ct)
    {
        var item = await db.EmailsFila.SingleAsync(e => e.Id == id, ct);
        if (item.Estado != EstadoEmail.Pendente) return;

        try
        {
            var anexos = new List<AnexoEmail>();
            foreach (var a in (item.Anexos ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var partes = a.Split('|');
                await using var stream = ficheiros.Abrir(partes[0]) ?? throw new FileNotFoundException("Anexo não encontrado: " + partes[0]);
                using var m = new MemoryStream();
                await stream.CopyToAsync(m, ct);
                anexos.Add(new AnexoEmail(partes.Length > 1 ? partes[1] : Path.GetFileName(partes[0]), m.ToArray()));
            }

            await enviador.EnviarAsync(new MensagemEmail(item.Para, item.Cc, item.Bcc, item.Assunto, ParaHtml(item.Corpo), anexos), ct);
            item.Estado = EstadoEmail.Enviado;
            item.EnviadoEm = relogio.GetUtcNow().UtcDateTime;
            item.ErroUltimo = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            item.Tentativas++;
            item.ErroUltimo = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            if (item.Tentativas > Esperas.Length) item.Estado = EstadoEmail.Falhou;
            else item.ProximaTentativa = relogio.GetUtcNow().UtcDateTime + Esperas[item.Tentativas - 1];
            log.LogWarning(ex, "Falha a enviar o email {Id} (tentativa {Tentativa}).", item.Id, item.Tentativas);
        }
        await db.SaveChangesAsync(ct);
    }

    public static string Substituir(string texto, IReadOnlyDictionary<string, string> variaveis) =>
        Variavel().Replace(texto, m => variaveis.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>Texto simples → HTML seguro (escapa tudo e converte quebras de linha).</summary>
    public static string ParaHtml(string texto) =>
        "<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;color:#1B2430\">"
        + WebUtility.HtmlEncode(texto).Replace("\r\n", "\n").Replace("\n", "<br>") + "</div>";

    private static List<string> Lista(string? emails) =>
        (emails ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Valor(decimal v)
    {
        var f = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        f.NumberGroupSeparator = ".";
        f.NumberDecimalSeparator = ",";
        return v.ToString("#,##0.00", f);
    }

    [GeneratedRegex(@"\{([a-z]+\.[a-z]+)\}")]
    private static partial Regex Variavel();
}
