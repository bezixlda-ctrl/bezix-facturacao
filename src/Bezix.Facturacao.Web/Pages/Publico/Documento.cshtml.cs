using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace Bezix.Facturacao.Web.Pages.Publico;

/// <summary>
/// Página que o cliente abre a partir do email, sem login: documento, PDF, contas bancárias, saldo em aberto
/// e formulário para comunicar um pagamento com comprovativo.
/// </summary>
[EnableRateLimiting("publico")]
[RequestSizeLimit(6 * 1024 * 1024)]
public class DocumentoModel(ServicoLinks links, ServicoPdfDocumentos pdfs, ServicoContaCorrente contaCorrente, ServicoEmpresa empresas,
    Infrastructure.Persistencia.AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";

    public LinkAberto Aberto { get; private set; } = null!;
    public Empresa Empresa { get; private set; } = null!;
    public List<ContaBancaria> Contas { get; private set; } = [];
    public SituacaoDocumento? Situacao { get; private set; }
    public decimal SaldoCliente { get; private set; }
    public bool Comunicado { get; private set; }
    public List<string> Erros { get; } = [];

    [BindProperty] public decimal Valor { get; set; }
    [BindProperty] public DateOnly Data { get; set; }
    [BindProperty] public string? Referencia { get; set; }
    [BindProperty] public string? Mensagem { get; set; }
    [BindProperty] public IFormFile? Comprovativo { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CarregarAsync()) return Expirado();
        await links.RegistarVisitaAsync(Aberto.Link, HttpContext.Connection.RemoteIpAddress?.ToString());
        Valor = Situacao?.EmAberto ?? 0;
        Data = contaCorrente.Hoje();
        Comunicado = TempData["Comunicado"] is true;
        return Page();
    }

    public async Task<IActionResult> OnGetPdfAsync()
    {
        if (!await CarregarAsync()) return Expirado();
        var pdf = await pdfs.ObterAsync(Aberto.Documento.Id);
        if (pdf is null) return Expirado();
        var disposicao = new ContentDispositionHeaderValue("inline");
        disposicao.SetHttpFileName(pdf.Nome);
        Response.Headers.ContentDisposition = disposicao.ToString();
        Response.Headers.CacheControl = "private, no-store";
        return File(pdf.Conteudo, "application/pdf");
    }

    public async Task<IActionResult> OnPostComunicarAsync()
    {
        if (!await CarregarAsync()) return Expirado();
        byte[]? ficheiro = null;
        if (Comprovativo is { Length: > 0 } f && f.Length <= ServicoLinks.TamanhoMaximoComprovativo)
        {
            using var m = new MemoryStream();
            await f.CopyToAsync(m);
            ficheiro = m.ToArray();
        }
        else if (Comprovativo is { Length: > 0 }) Erros.Add("O comprovativo tem mais de 5 MB.");

        if (Erros.Count == 0)
        {
            var r = await links.ComunicarPagamentoAsync(Aberto, Valor, Data, Referencia, Mensagem, ficheiro, HttpContext.Connection.RemoteIpAddress?.ToString());
            if (r.Sucesso)
            {
                TempData["Comunicado"] = true;
                return RedirectToPage(new { Token });
            }
            Erros.AddRange(r.Erros);
        }
        return Page();
    }

    private async Task<bool> CarregarAsync()
    {
        var a = await links.AbrirAsync(Token);
        if (a is null) return false;
        Aberto = a;
        Empresa = await empresas.ObterAsync();
        Contas = db.ContasBancarias.Where(c => c.MostrarNoDocumento).OrderBy(c => c.Ordem).ToList();
        Situacao = (await contaCorrente.SituacaoAsync([a.Documento.Id])).GetValueOrDefault(a.Documento.Id);
        SaldoCliente = (await contaCorrente.SaldoAsync(a.Cliente.Id)).Saldo;
        return true;
    }

    private IActionResult Expirado()
    {
        Response.StatusCode = 404;
        return Page();
    }

    public bool Valido => Aberto is not null;
    public bool PodeComunicar => Situacao is { EmAberto: > 0 } && Aberto.Documento.Estado == EstadoDocumento.Emitido;
}
