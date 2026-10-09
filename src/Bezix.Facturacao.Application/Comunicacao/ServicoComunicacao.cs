using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Integracoes;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bezix.Facturacao.Application.Comunicacao;

public sealed record EmailPreparado(string Para, string? Cc, string Assunto, string Corpo, bool AnexarPdf);

public sealed record EmailVista(int Id, string Para, string Assunto, EstadoEmail Estado, int Tentativas, string? Erro, DateTime CriadoEm, DateTime? EnviadoEm,
    int? ClienteId, int? DocumentoId, string? Modelo);

/// <summary>
/// Emails para clientes: modelos editáveis com variáveis, fila na base de dados e repetições.
/// Envia logo que é pedido; o que falhar é repetido pelo jobs/tick (1, 5, 30 min, 2 h, 12 h).
/// </summary>
public sealed partial class ServicoComunicacao(
    IAppDbContext db,
    IEnviadorEmail enviador,
    IArmazenamentoFicheiros ficheiros,
    ServicoEmpresa empresas,
    ServicoPdfDocumentos pdfs,
    ServicoLinks links,
    Recebimentos.ServicoContaCorrente contaCorrente,
    IPublicadorEventos eventos,
    IAssinaturas assinaturas,
    IUtilizadorActual utilizador,
    IServicoAuditoria auditoria,
    TimeProvider relogio,
    ILogger<ServicoComunicacao> log)
{
    public static readonly TimeSpan[] Esperas =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12)];

    public const string CodigoModeloExtracto = "extracto";

    public static string CodigoModelo(TipoDocumento t) => $"documento.{t}";

    // ------------------------------------------------------------------ Variáveis

    private async Task<Dictionary<string, string>> VariaveisBaseAsync(Cliente? cliente, CancellationToken ct)
    {
        var empresa = await empresas.ObterAsync(ct);
        var contas = await db.ContasBancarias.AsNoTracking().Where(c => c.MostrarNoDocumento).OrderBy(c => c.Ordem).ToListAsync(ct);
        var assinaturaUtilizador = utilizador.Id is { } uid ? await assinaturas.DoUtilizadorAsync(uid, ct) : null;

        var nomeUtilizador = utilizador.Nome ?? empresa.Nome;
        return new Dictionary<string, string>
        {
            ["cliente.nome"] = cliente?.Nome ?? "",
            ["cliente.contacto"] = cliente?.Contacto ?? cliente?.Nome ?? "",
            ["empresa.nome"] = empresa.Nome,
            ["empresa.contas"] = contas.Count == 0 ? "" : string.Join("\n", contas.Select(c => $"{c.Banco}: {c.NumeroConta}" + (c.Nib is null ? "" : $" · NIB {c.Nib}"))),
            ["utilizador.nome"] = nomeUtilizador,
            ["assinatura"] = assinaturaUtilizador ?? empresa.AssinaturaEmail ?? $"{nomeUtilizador}\n{empresa.Nome}",
            ["link.documento"] = "",
        };
    }

    private async Task<Dictionary<string, string>> VariaveisDocumentoAsync(Documento doc, Cliente cliente, bool comLink, CancellationToken ct)
    {
        var v = await VariaveisBaseAsync(cliente, ct);
        var situacao = (await contaCorrente.SituacaoAsync([doc.Id], ct)).GetValueOrDefault(doc.Id);
        var saldo = await contaCorrente.SaldoAsync(cliente.Id, ct);
        v["documento.tipo"] = TipoDocumentoInfo.Nome(doc.TipoDoc).ToLowerInvariant();
        v["documento.numero"] = doc.NumeroFormatado ?? "(rascunho)";
        v["documento.data"] = doc.Data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        v["documento.vencimento"] = doc.Vencimento?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
        v["documento.total"] = Valor(doc.Total) + " MZN";
        v["documento.emaberto"] = Valor(situacao?.EmAberto ?? 0) + " MZN";
        v["dias.atraso"] = (situacao?.DiasAtraso ?? 0).ToString(CultureInfo.InvariantCulture);
        v["saldo.vencido"] = Valor(saldo.Vencido) + " MZN";
        if (comLink && doc.Emitido) v["link.documento"] = await links.CriarAsync(doc.Id, ct);
        return v;
    }

    // ------------------------------------------------------------------ Documentos

    /// <summary>Email proposto para um documento, a partir do modelo do tipo de documento.</summary>
    public Task<EmailPreparado?> PrepararDocumentoAsync(int documentoId, CancellationToken ct = default) =>
        PrepararAsync(documentoId, null, ct);

    public async Task<EmailPreparado?> PrepararAsync(int documentoId, string? codigoModelo, CancellationToken ct = default)
    {
        var doc = await db.Documentos.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentoId, ct);
        if (doc is null) return null;
        var cliente = await db.Clientes.AsNoTracking().SingleAsync(c => c.Id == doc.ClienteId, ct);
        var codigo = codigoModelo ?? CodigoModelo(doc.TipoDoc);
        var modelo = await ModeloActivoAsync(codigo, ct);
        var usaLink = (modelo.Assunto + modelo.Corpo).Contains("{link.documento}");
        var v = await VariaveisDocumentoAsync(doc, cliente, usaLink, ct);
        var cc = string.Join(", ", new[] { modelo.Cc, cliente.EmailCc }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new EmailPreparado(cliente.EmailEnvio ?? "", cc.Length == 0 ? null : cc, Substituir(modelo.Assunto, v), Substituir(modelo.Corpo, v), modelo.AnexarPdf);
    }

    /// <summary>Põe o email na fila com o PDF do documento (o mesmo ficheiro da impressão) e tenta enviar já.</summary>
    public async Task<Resultado> EnviarDocumentoAsync(int documentoId, EmailPreparado email, CancellationToken ct = default)
    {
        var tipo = await db.Documentos.AsNoTracking().Where(d => d.Id == documentoId).Select(d => (TipoDocumento?)d.TipoDoc).SingleOrDefaultAsync(ct);
        if (tipo is null) return Resultado.Falha("Documento não encontrado.");
        var (r, _) = await EnviarDocumentoInternoAsync(documentoId, email, CodigoModelo(tipo.Value), ct);
        return r;
    }

    /// <summary>Envia ao cliente um email de um modelo (lembretes, recorrentes, confirmações), sem intervenção do utilizador.</summary>
    public async Task<(Resultado Resultado, int? EmailId)> EnviarComModeloAsync(int documentoId, string codigoModelo, CancellationToken ct = default)
    {
        var email = await PrepararAsync(documentoId, codigoModelo, ct);
        if (email is null) return (Resultado.Falha("Documento não encontrado."), null);
        if (string.IsNullOrWhiteSpace(email.Para)) return (Resultado.Falha("O cliente não tem email."), null);
        return await EnviarDocumentoInternoAsync(documentoId, email, codigoModelo, ct);
    }

    private async Task<(Resultado, int?)> EnviarDocumentoInternoAsync(int documentoId, EmailPreparado email, string codigoModelo, CancellationToken ct)
    {
        var doc = await db.Documentos.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentoId, ct);
        if (doc is null) return (Resultado.Falha("Documento não encontrado."), null);
        if (doc.Estado != EstadoDocumento.Emitido) return (Resultado.Falha("Só documentos emitidos (e não anulados) podem ser enviados."), null);

        var erros = ValidarDestinatarios(email);
        if (erros.Count > 0) return (Resultado.Falha([.. erros]), null);

        string? anexos = null;
        if (email.AnexarPdf)
        {
            await pdfs.GarantirPdfGuardadoAsync(documentoId, ct);
            var caminho = await db.Documentos.AsNoTracking().Where(d => d.Id == documentoId).Select(d => d.PdfPath).SingleAsync(ct);
            anexos = $"{caminho}|{doc.NomeFicheiroPdf()}";
        }

        var item = NovoEmail(email, anexos, doc.ClienteId, doc.Id, codigoModelo);
        db.EmailsFila.Add(item);
        auditoria.Registar(nameof(Documento), doc.Id.ToString(), "Documento.Enviar", null, new { doc.NumeroFormatado, item.Para, item.Cc, Modelo = codigoModelo });
        eventos.Publicar(EventosWebhook.DocumentoEnviado, new { id = doc.Id, tipo = doc.TipoDoc.ToString(), numero = doc.NumeroFormatado, clienteId = doc.ClienteId, para = item.Para, modelo = codigoModelo });
        await db.SaveChangesAsync(ct);
        return (await TentarEnviarEResultadoAsync(item.Id, ct), item.Id);
    }

    public Task<List<EmailFila>> HistoricoDocumentoAsync(int documentoId, CancellationToken ct = default) =>
        db.EmailsFila.AsNoTracking().Where(e => e.DocumentoId == documentoId).OrderByDescending(e => e.Id).ToListAsync(ct);

    public Task<List<EmailFila>> HistoricoClienteAsync(int clienteId, CancellationToken ct = default) =>
        db.EmailsFila.AsNoTracking().Where(e => e.ClienteId == clienteId).OrderByDescending(e => e.Id).Take(20).ToListAsync(ct);

    // ------------------------------------------------------------------ Extractos

    /// <summary>Email proposto para enviar o extracto de conta corrente de um período.</summary>
    public async Task<EmailPreparado?> PrepararExtractoAsync(int clienteId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var extracto = await contaCorrente.ExtractoAsync(clienteId, de, ate, ct);
        if (extracto is null) return null;
        var cliente = await db.Clientes.AsNoTracking().SingleAsync(c => c.Id == clienteId, ct);
        var saldo = await contaCorrente.SaldoAsync(clienteId, ct);
        var modelo = await ModeloActivoAsync(CodigoModeloExtracto, ct);
        var v = await VariaveisBaseAsync(cliente, ct);
        v["extracto.periodo"] = $"{de:dd/MM/yyyy} a {ate:dd/MM/yyyy}";
        v["saldo.final"] = Valor(extracto.SaldoFinal) + " MZN";
        v["saldo.vencido"] = Valor(saldo.Vencido) + " MZN";
        var cc = string.Join(", ", new[] { modelo.Cc, cliente.EmailCc }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new EmailPreparado(cliente.EmailEnvio ?? "", cc.Length == 0 ? null : cc, Substituir(modelo.Assunto, v), Substituir(modelo.Corpo, v), true);
    }

    public async Task<Resultado> EnviarExtractoAsync(int clienteId, DateOnly de, DateOnly ate, EmailPreparado email, CancellationToken ct = default)
    {
        var extracto = await contaCorrente.ExtractoAsync(clienteId, de, ate, ct);
        if (extracto is null) return Resultado.Falha("Cliente não encontrado.");
        var erros = ValidarDestinatarios(email);
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        var pdf = await pdfs.ExtractoAsync(extracto, ct);
        var caminho = await ficheiros.GuardarAsync("extractos", ".pdf", pdf, ct);
        var item = NovoEmail(email, $"{caminho}|Extracto_{de:yyyyMMdd}-{ate:yyyyMMdd}.pdf", clienteId, null, CodigoModeloExtracto);
        db.EmailsFila.Add(item);
        auditoria.Registar("Cliente", clienteId.ToString(), "Extracto.Enviar", null, new { De = de, Ate = ate, item.Para, extracto.SaldoFinal });
        await db.SaveChangesAsync(ct);
        return await TentarEnviarEResultadoAsync(item.Id, ct);
    }

    // ------------------------------------------------------------------ Modelos

    public Task<List<ModeloComunicacao>> ModelosAsync(CancellationToken ct = default) =>
        db.ModelosComunicacao.AsNoTracking().OrderBy(m => m.Codigo).ToListAsync(ct);

    public Task<ModeloComunicacao?> ModeloAsync(string codigo, CancellationToken ct = default) =>
        db.ModelosComunicacao.AsNoTracking().SingleOrDefaultAsync(m => m.Codigo == codigo, ct);

    public async Task<Resultado> GuardarModeloAsync(string codigo, string assunto, string corpo, string? cc, bool anexarPdf, bool activo, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Comunicacao.GerirModelos)) return Resultado.Falha("O seu perfil não permite editar modelos.");
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(assunto)) erros.Add("Indique o assunto.");
        if (string.IsNullOrWhiteSpace(corpo)) erros.Add("Escreva o texto do email.");
        if (assunto?.Length > 300) erros.Add("O assunto pode ter no máximo 300 caracteres.");
        if (!string.IsNullOrWhiteSpace(cc) && Lista(cc).Any(e => !Clientes.ServicoClientes.EmailValido(e))) erros.Add("Um dos emails em cópia não é válido.");
        var desconhecidas = Variavel().Matches((assunto ?? "") + (corpo ?? "")).Select(m => "{" + m.Groups[1].Value + "}")
            .Where(v => ModelosPadrao.Variaveis.All(x => x.Nome != v)).Distinct().ToList();
        if (desconhecidas.Count > 0) erros.Add("Variáveis desconhecidas: " + string.Join(", ", desconhecidas));
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        var m = await db.ModelosComunicacao.SingleOrDefaultAsync(x => x.Codigo == codigo, ct);
        if (m is null) return Resultado.Falha("Modelo não encontrado.");
        var antes = new { m.Assunto, m.Corpo, m.Cc, m.AnexarPdf, m.Activo };
        m.Assunto = assunto!.Trim();
        m.Corpo = corpo!.Replace("\r\n", "\n").Trim();
        m.Cc = string.IsNullOrWhiteSpace(cc) ? null : cc.Trim();
        m.AnexarPdf = anexarPdf;
        m.Activo = activo;
        auditoria.Registar(nameof(ModeloComunicacao), codigo, "Modelo.Alterar", antes, new { m.Assunto, m.Corpo, m.Cc, m.AnexarPdf, m.Activo });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> ReporModeloAsync(string codigo, CancellationToken ct = default)
    {
        var original = ModelosPadrao.Obter(codigo);
        if (original is null) return Resultado.Falha("Este modelo não tem texto original.");
        return await GuardarModeloAsync(codigo, original.Assunto, original.Corpo, null, original.AnexarPdf, true, ct);
    }

    /// <summary>Pré-visualização com dados reais: o último documento emitido do tipo do modelo (ou de qualquer tipo).</summary>
    public async Task<(string Assunto, string CorpoHtml, string? Origem)> PrevisualizarAsync(string codigo, string assunto, string corpo, CancellationToken ct = default)
    {
        var tipo = codigo.StartsWith("documento.") && Enum.TryParse<TipoDocumento>(codigo[10..], out var t) ? t : TipoDocumento.FT;
        var doc = await db.Documentos.AsNoTracking().Where(d => d.Estado == EstadoDocumento.Emitido)
            .OrderByDescending(d => d.TipoDoc == tipo).ThenByDescending(d => d.Id).FirstOrDefaultAsync(ct);
        Dictionary<string, string> v;
        if (doc is null)
        {
            v = await VariaveisBaseAsync(new Cliente { Nome = "Cliente Exemplo, Lda", Contacto = "Sr. Exemplo" }, ct);
            v["documento.tipo"] = "factura"; v["documento.numero"] = "FT A2026/0001"; v["documento.data"] = "01/10/2026";
            v["documento.vencimento"] = "31/10/2026"; v["documento.total"] = "28.586,20 MZN"; v["documento.emaberto"] = "28.586,20 MZN";
            v["dias.atraso"] = "8"; v["saldo.vencido"] = "28.586,20 MZN"; v["link.documento"] = "https://…/p/exemplo";
        }
        else
        {
            var cliente = await db.Clientes.AsNoTracking().SingleAsync(c => c.Id == doc.ClienteId, ct);
            v = await VariaveisDocumentoAsync(doc, cliente, comLink: false, ct);
            v["link.documento"] = "https://…/p/(link criado no envio)";
        }
        v["extracto.periodo"] = "01/01/2026 a 09/10/2026";
        v["saldo.final"] = v.GetValueOrDefault("saldo.vencido", "0,00 MZN");
        return (Substituir(assunto, v), ParaHtml(Substituir(corpo, v)), doc?.NumeroFormatado);
    }

    /// <summary>Envia a pré-visualização para o email de quem está a editar.</summary>
    public async Task<Resultado> EnviarTesteAsync(string codigo, string assunto, string corpo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(utilizador.Email)) return Resultado.Falha("Sem email de utilizador.");
        var (a, html, _) = await PrevisualizarAsync(codigo, assunto, corpo, ct);
        try
        {
            await enviador.EnviarAsync(new MensagemEmail(utilizador.Email, null, null, "[Teste] " + a, html, []), ct);
            return Resultado.Ok();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Resultado.Falha("Não foi possível enviar: " + ex.Message);
        }
    }

    private async Task<ModeloComunicacao> ModeloActivoAsync(string codigo, CancellationToken ct) =>
        await db.ModelosComunicacao.AsNoTracking().SingleOrDefaultAsync(m => m.Codigo == codigo && m.Activo, ct)
        ?? ModelosPadrao.Obter(codigo)
        ?? new ModeloComunicacao { Codigo = codigo, Assunto = "{documento.tipo} {documento.numero} · {empresa.nome}", Corpo = "Exmo(a). {cliente.contacto},\n\nSegue em anexo {documento.tipo} {documento.numero}.\n\n{assinatura}" };

    // ------------------------------------------------------------------ Fila

    public async Task<Pagina<EmailVista>> EmailsAsync(EstadoEmail? estado, int pagina, CancellationToken ct = default)
    {
        var (numero, tamanho) = Pagina<EmailVista>.Normalizar(pagina, 50);
        var q = db.EmailsFila.AsNoTracking();
        if (estado is { } e) q = q.Where(x => x.Estado == e);
        var total = await q.CountAsync(ct);
        var itens = await q.OrderByDescending(x => x.Id).Skip((numero - 1) * tamanho).Take(tamanho)
            .Select(x => new EmailVista(x.Id, x.Para, x.Assunto, x.Estado, x.Tentativas, x.ErroUltimo, x.CreatedAt, x.EnviadoEm, x.ClienteId, x.DocumentoId, x.ModeloCodigo))
            .ToListAsync(ct);
        return new Pagina<EmailVista>(itens, total, numero, tamanho);
    }

    /// <summary>Volta a pôr um email falhado na fila e tenta já.</summary>
    public async Task<Resultado> ReenviarAsync(int id, CancellationToken ct = default)
    {
        var e = await db.EmailsFila.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return Resultado.Falha("Email não encontrado.");
        if (e.Estado == EstadoEmail.Enviado) return Resultado.Falha("Este email já foi enviado.");
        e.Estado = EstadoEmail.Pendente;
        e.Tentativas = 0;
        e.ProximaTentativa = relogio.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return await TentarEnviarEResultadoAsync(id, ct);
    }

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

    private EmailFila NovoEmail(EmailPreparado email, string? anexos, int? clienteId, int? documentoId, string modelo) => new()
    {
        Para = string.Join(", ", Lista(email.Para)),
        Cc = Lista(email.Cc) is { Count: > 0 } cc ? string.Join(", ", cc) : null,
        Assunto = email.Assunto.Trim(),
        Corpo = email.Corpo,
        Anexos = anexos,
        ClienteId = clienteId,
        DocumentoId = documentoId,
        ModeloCodigo = modelo,
        ProximaTentativa = relogio.GetUtcNow().UtcDateTime,
    };

    private static List<string> ValidarDestinatarios(EmailPreparado email)
    {
        var erros = new List<string>();
        var destinatarios = Lista(email.Para);
        if (destinatarios.Count == 0) erros.Add("Indique o email do destinatário.");
        if (destinatarios.Concat(Lista(email.Cc)).Any(e => !Clientes.ServicoClientes.EmailValido(e))) erros.Add("Um dos emails não é válido.");
        if (string.IsNullOrWhiteSpace(email.Assunto)) erros.Add("Indique o assunto.");
        return erros;
    }

    private async Task<Resultado> TentarEnviarEResultadoAsync(int id, CancellationToken ct)
    {
        await TentarEnviarAsync(id, ct);
        var estado = await db.EmailsFila.AsNoTracking().Where(e => e.Id == id).Select(e => new { e.Estado, e.ErroUltimo }).SingleAsync(ct);
        return estado.Estado == EstadoEmail.Enviado
            ? Resultado.Ok()
            : Resultado.Falha($"O email ficou na fila e vai ser repetido automaticamente. Erro: {estado.ErroUltimo}");
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

    // ------------------------------------------------------------------ Texto

    public static string Substituir(string texto, IReadOnlyDictionary<string, string> variaveis) =>
        Variavel().Replace(texto, m => variaveis.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>Texto simples → HTML seguro (escapa tudo, converte quebras de linha e torna os links clicáveis).</summary>
    public static string ParaHtml(string texto)
    {
        var html = WebUtility.HtmlEncode(texto).Replace("\r\n", "\n").Replace("\n", "<br>");
        html = Url().Replace(html, m => $"<a href=\"{m.Value}\">{m.Value}</a>");
        return "<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;color:#1B2430\">" + html + "</div>";
    }

    private static List<string> Lista(string? emails) =>
        (emails ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static string Valor(decimal v)
    {
        var f = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        f.NumberGroupSeparator = ".";
        f.NumberDecimalSeparator = ",";
        return v.ToString("#,##0.00", f);
    }

    [GeneratedRegex(@"\{([a-z]+\.[a-z]+|assinatura)\}")]
    private static partial Regex Variavel();

    [GeneratedRegex(@"https://[^\s<]+")]
    private static partial Regex Url();
}
