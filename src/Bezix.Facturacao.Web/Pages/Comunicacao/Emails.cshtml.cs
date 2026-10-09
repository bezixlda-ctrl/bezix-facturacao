using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Comunicacao;

[Authorize(Policy = Permissoes.Comunicacao.Enviar)]
public class EmailsModel(ServicoComunicacao comunicacao) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public EstadoEmail? Estado { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int NumeroPagina { get; set; } = 1;

    public Pagina<EmailVista> Resultado { get; private set; } = new([], 0, 1, 50);

    public async Task OnGetAsync() => Resultado = await comunicacao.EmailsAsync(Estado, NumeroPagina);

    public async Task<IActionResult> OnPostReenviarAsync(int id)
    {
        var r = await comunicacao.ReenviarAsync(id);
        if (r.Sucesso) Sucesso("Email reenviado.");
        else Erro(string.Join(" ", r.Erros));
        return RedirectToPage(new { Estado, p = NumeroPagina == 1 ? (int?)null : NumeroPagina });
    }

    public Paginacao Paginacao => Paginacao.De(Resultado,
        p => Url.Page("/Comunicacao/Emails", new { Estado, p = p == 1 ? (int?)null : p })!, null);
}
