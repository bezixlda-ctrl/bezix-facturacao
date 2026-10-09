using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Bezix.Facturacao.Web.Pages.ContaCorrente;

[Authorize(Policy = Permissoes.Clientes.Ver)]
public class ClienteModel(
    ServicoContaCorrente contaCorrente,
    ServicoRecibos recibos,
    ServicoPdfDocumentos pdfs,
    ServicoComunicacao comunicacao) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? De { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Ate { get; set; }

    public Extracto Extracto { get; private set; } = null!;
    public SaldoCliente Saldo { get; private set; } = null!;
    public List<DocumentoAberto> Abertos { get; private set; } = [];
    public List<PagamentoVista> Adiantamentos { get; private set; } = [];
    public EmailPreparado? Email { get; private set; }
    public bool PodeRegistar => User.TemPermissao(Permissoes.Recibos.Registar);
    public bool PodeEnviar => User.TemPermissao(Permissoes.Comunicacao.Enviar);

    // Alocar adiantamento
    [BindProperty] public int PagamentoId { get; set; }
    [BindProperty] public List<AlocacaoForm> Alocacoes { get; set; } = [];
    public sealed class AlocacaoForm { public int DocumentoId { get; set; } public decimal Valor { get; set; } }

    // Envio do extracto
    [BindProperty] public string? Para { get; set; }
    [BindProperty] public string? Cc { get; set; }
    [BindProperty] public string? Assunto { get; set; }
    [BindProperty] public string? Corpo { get; set; }

    private DateOnly Inicio => De ?? new DateOnly(contaCorrente.Hoje().Year, 1, 1);
    private DateOnly Fim => Ate ?? contaCorrente.Hoje();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CarregarAsync()) return NotFound();
        if (PodeEnviar) Email = await comunicacao.PrepararExtractoAsync(Id, Inicio, Fim);
        return Page();
    }

    public async Task<IActionResult> OnGetPdfAsync(bool descarregar = false)
    {
        var extracto = await contaCorrente.ExtractoAsync(Id, Inicio, Fim);
        if (extracto is null) return NotFound();
        var bytes = await pdfs.ExtractoAsync(extracto);
        var disposicao = new ContentDispositionHeaderValue(descarregar ? "attachment" : "inline");
        disposicao.SetHttpFileName($"Extracto_{extracto.ClienteNome.Replace(' ', '_')}_{Inicio:yyyyMMdd}-{Fim:yyyyMMdd}.pdf");
        Response.Headers.ContentDisposition = disposicao.ToString();
        return File(bytes, "application/pdf");
    }

    public async Task<IActionResult> OnPostEnviarAsync()
    {
        if (!PodeEnviar) return Forbid();
        var r = await comunicacao.EnviarExtractoAsync(Id, Inicio, Fim, new EmailPreparado(Para ?? "", Cc, Assunto ?? "", Corpo ?? "", true));
        if (r.Sucesso) Sucesso($"Extracto enviado para {Para}.");
        else Erro(r.Erros[0]);
        return RedirectToPage(new { Id, De = Inicio, Ate = Fim });
    }

    public async Task<IActionResult> OnPostAlocarAsync()
    {
        var r = await recibos.AlocarAdiantamentoAsync(PagamentoId,
            Alocacoes.Where(a => a.Valor > 0).Select(a => new Alocacao(a.DocumentoId, a.Valor)).ToList());
        if (r.Sucesso) Sucesso("Adiantamento alocado.");
        else Erro(string.Join(" ", r.Erros));
        return RedirectToPage(new { Id });
    }

    private async Task<bool> CarregarAsync()
    {
        var extracto = await contaCorrente.ExtractoAsync(Id, Inicio, Fim);
        if (extracto is null) return false;
        Extracto = extracto;
        Saldo = await contaCorrente.SaldoAsync(Id);
        Abertos = await contaCorrente.EmAbertoAsync(Id);
        Adiantamentos = await recibos.AdiantamentosAsync(Id);
        return true;
    }
}
