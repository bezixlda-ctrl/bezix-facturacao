using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Integracoes;
using Bezix.Facturacao.Domain.Integracoes;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Administracao.Integracoes;

[Authorize(Policy = Permissoes.Administracao.Integracoes)]
public class EntregasModel(ServicoWebhooks webhooks) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public EstadoEntrega? Estado { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int NumeroPagina { get; set; } = 1;

    public Pagina<WebhookEntrega> Resultado { get; private set; } = new([], 0, 1, 50);
    public VistaEntregas VistaEntregas => new(Resultado.Itens, true, "Entregas");

    public async Task OnGetAsync() => Resultado = await webhooks.EntregasAsync(null, Estado, NumeroPagina);

    public async Task<IActionResult> OnPostReenviarAsync(long entrega)
    {
        var r = await webhooks.ReenviarAsync(entrega);
        if (r.Sucesso) Sucesso("Entrega repetida.");
        else Erro(string.Join(" ", r.Erros));
        return RedirectToPage(new { Estado });
    }

    public Paginacao Paginacao => Paginacao.De(Resultado,
        p => Url.Page("/Administracao/Integracoes/Entregas", new { Estado, p = p == 1 ? (int?)null : p })!, null);
}
