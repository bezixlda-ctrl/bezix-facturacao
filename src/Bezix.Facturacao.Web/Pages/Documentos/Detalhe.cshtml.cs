using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Domain.Auditoria;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Bezix.Facturacao.Web.Pages.Documentos;

[Authorize(Policy = Permissoes.Documentos.Ver)]
public class DetalheModel(
    ServicoDocumentos documentos,
    ServicoEmissao emissao,
    ServicoPdfDocumentos pdfs,
    ServicoComunicacao comunicacao,
    Application.Recebimentos.ServicoContaCorrente contaCorrente,
    Application.Recebimentos.ServicoRecibos recibos,
    AppDbContext db) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public Documento Documento { get; private set; } = null!;
    public List<DocumentoResumo> Correccoes { get; private set; } = [];
    public List<DocumentoResumo> Convertidos { get; private set; } = [];
    public List<Aprovacao> Aprovacoes { get; private set; } = [];
    public List<EmailFila> Emails { get; private set; } = [];
    public List<RegistoAuditoria> Historico { get; private set; } = [];
    public EmailPreparado? EmailProposto { get; private set; }
    public decimal? PorCreditar { get; private set; }
    public Application.Recebimentos.SituacaoDocumento? Situacao { get; private set; }
    public List<(int ReciboId, string Numero, DateOnly Data, decimal Valor)> Pagamentos { get; private set; } = [];
    public Application.Recebimentos.PagamentoVista? PagamentoDoRecibo { get; private set; }
    public bool PodeRegistarRecibo => Situacao is { EmAberto: > 0 } && User.TemPermissao(Permissoes.Recibos.Registar);

    // Envio por email
    [BindProperty] public string? Para { get; set; }
    [BindProperty] public string? Cc { get; set; }
    [BindProperty] public string? Assunto { get; set; }
    [BindProperty] public string? Corpo { get; set; }

    public bool PodeEditar => Documento.Estado == EstadoDocumento.Rascunho && documentos.PodeEditar(Documento.TipoDoc);
    public bool PodeAnular => Documento.Estado == EstadoDocumento.Emitido && User.TemPermissao(Permissoes.Documentos.Anular);
    public bool PodeAprovar => Documento.Estado == EstadoDocumento.AguardaAprovacao && User.TemPermissao(Permissoes.Documentos.Aprovar);
    public bool PodeEnviar => Documento.Estado == EstadoDocumento.Emitido && User.TemPermissao(Permissoes.Comunicacao.Enviar);
    public bool PodeCriarNc => Documento.Estado == EstadoDocumento.Emitido && RegrasTipo.OrigemValida(TipoDocumento.NC, Documento.TipoDoc)
        && documentos.PodeEditar(TipoDocumento.NC) && PorCreditar > 0;
    public bool PodeCriarNd => Documento.Estado == EstadoDocumento.Emitido && RegrasTipo.OrigemValida(TipoDocumento.ND, Documento.TipoDoc)
        && documentos.PodeEditar(TipoDocumento.ND);
    public IEnumerable<TipoDocumento> ConversoesPossiveis => Documento.Estado == EstadoDocumento.Emitido
        ? RegrasTipo.ConverteEm(Documento.TipoDoc).Where(documentos.PodeEditar) : [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CarregarAsync()) return NotFound();
        if (PodeEnviar) EmailProposto = await comunicacao.PrepararDocumentoAsync(Id);
        return Page();
    }

    /// <summary>PDF: inline para imprimir no browser, ou attachment para descarregar.</summary>
    public async Task<IActionResult> OnGetPdfAsync(bool descarregar = false, bool via = false)
    {
        if (await documentos.ObterAsync(Id) is null) return NotFound();
        var pdf = await pdfs.ObterAsync(Id, via);
        if (pdf is null) return NotFound();
        var nome = via ? pdf.Nome.Replace(".pdf", "_2via.pdf") : pdf.Nome;
        var disposicao = new ContentDispositionHeaderValue(descarregar ? "attachment" : "inline");
        disposicao.SetHttpFileName(nome);
        Response.Headers.ContentDisposition = disposicao.ToString();
        Response.Headers.CacheControl = "private, no-store";
        return File(pdf.Conteudo, "application/pdf");
    }

    public async Task<IActionResult> OnPostEmitirAsync()
    {
        var r = await emissao.EmitirAsync(Id);
        return r.Tipo switch
        {
            ResultadoEmissaoTipo.Emitido => RedirecionarComSucesso($"Documento {r.NumeroFormatado} emitido.", null, new { Id }),
            _ => ComErro(string.Join(" ", r.Resultado.Erros)),
        };
    }

    public async Task<IActionResult> OnPostAprovarAsync()
    {
        var r = await emissao.AprovarAsync(Id);
        return r.Tipo == ResultadoEmissaoTipo.Emitido
            ? RedirecionarComSucesso($"Aprovado e emitido com o número {r.NumeroFormatado}.", null, new { Id })
            : ComErro(string.Join(" ", r.Resultado.Erros));
    }

    public async Task<IActionResult> OnPostRejeitarAsync(string? nota)
    {
        var r = await emissao.DevolverARascunhoAsync(Id, nota, rejeicao: true);
        return r.Sucesso ? RedirecionarComSucesso("Pedido rejeitado. O documento voltou a rascunho.", null, new { Id }) : ComErro(r.Erros[0]);
    }

    public async Task<IActionResult> OnPostCancelarPedidoAsync()
    {
        var r = await emissao.DevolverARascunhoAsync(Id, null, rejeicao: false);
        return r.Sucesso ? RedirecionarComSucesso("Pedido de aprovação cancelado. O documento voltou a rascunho.", null, new { Id }) : ComErro(r.Erros[0]);
    }

    public async Task<IActionResult> OnPostAnularAsync(string? motivo)
    {
        var r = await emissao.AnularAsync(Id, motivo ?? "");
        return r.Sucesso ? RedirecionarComSucesso("Documento anulado. O número mantém-se e fica registado.", null, new { Id }) : ComErro(r.Erros[0]);
    }

    public async Task<IActionResult> OnPostDuplicarAsync()
    {
        var (r, novo) = await documentos.DuplicarAsync(Id);
        return r.Sucesso ? RedirecionarComSucesso("Cópia criada como rascunho.", "Editar", new { id = novo }) : ComErro(r.Erros[0]);
    }

    public async Task<IActionResult> OnPostConverterAsync(TipoDocumento destino)
    {
        var (r, novo) = await documentos.ConverterAsync([Id], destino);
        return r.Sucesso ? RedirecionarComSucesso($"{TipoDocumentoInfo.Nome(destino)} criada como rascunho a partir deste documento.", "Editar", new { id = novo }) : ComErro(r.Erros[0]);
    }

    public async Task<IActionResult> OnPostCorreccaoAsync(TipoDocumento tipo)
    {
        var (r, novo) = await documentos.CriarCorreccaoAsync(Id, tipo);
        return r.Sucesso ? RedirecionarComSucesso($"{TipoDocumentoInfo.Nome(tipo)} criada como rascunho. Ajuste as linhas e emita.", "Editar", new { id = novo }) : ComErro(r.Erros[0]);
    }

    public async Task<IActionResult> OnPostApagarAsync()
    {
        var r = await documentos.ApagarRascunhoAsync(Id);
        return r.Sucesso ? RedirecionarComSucesso("Rascunho apagado.", "Index") : ComErro(r.Erros[0]);
    }

    public async Task<IActionResult> OnPostEnviarAsync()
    {
        if (!User.TemPermissao(Permissoes.Comunicacao.Enviar)) return Forbid();
        var r = await comunicacao.EnviarDocumentoAsync(Id, new EmailPreparado(Para ?? "", Cc, Assunto ?? "", Corpo ?? "", AnexarPdf: true));
        return r.Sucesso ? RedirecionarComSucesso($"Email enviado para {Para}.", null, new { Id }) : ComErro(r.Erros[0]);
    }

    private IActionResult ComErro(string mensagem)
    {
        Erro(mensagem);
        return RedirectToPage(new { Id });
    }

    private async Task<bool> CarregarAsync()
    {
        var d = await documentos.ObterAsync(Id);
        if (d is null) return false;
        Documento = d;
        Correccoes = await documentos.CorreccoesAsync(Id);
        Convertidos = await documentos.ConvertidosEmAsync(Id);
        Aprovacoes = await emissao.AprovacoesDoDocumentoAsync(Id);
        Emails = await comunicacao.HistoricoDocumentoAsync(Id);
        if (RegrasTipo.OrigemValida(TipoDocumento.NC, d.TipoDoc) && d.Estado == EstadoDocumento.Emitido)
            PorCreditar = await documentos.ValorPorCreditarAsync(Id);
        Situacao = (await contaCorrente.SituacaoAsync([Id])).GetValueOrDefault(Id);
        if (Situacao is not null) Pagamentos = await recibos.PagamentosDoDocumentoAsync(Id);
        if (d.TipoDoc == TipoDocumento.RC) PagamentoDoRecibo = await recibos.PagamentoDoReciboAsync(Id);
        if (User.TemPermissao(Permissoes.Administracao.VerAuditoria))
            Historico = await db.Auditoria.AsNoTracking()
                .Where(a => a.Entidade == nameof(Domain.Documentos.Documento) && a.EntidadeId == Id.ToString())
                .OrderByDescending(a => a.Id).Take(30).ToListAsync();
        return true;
    }
}
