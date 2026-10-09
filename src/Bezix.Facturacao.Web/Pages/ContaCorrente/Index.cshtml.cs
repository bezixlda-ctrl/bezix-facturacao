using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.ContaCorrente;

[Authorize(Policy = Permissoes.Clientes.Ver)]
public class IndexModel(ServicoContaCorrente contaCorrente) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool Todos { get; set; }
    [BindProperty(SupportsGet = true)] public bool SoVencidos { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int NumeroPagina { get; set; } = 1;

    public Pagina<ClienteSaldoLinha> Resultado { get; private set; } = new([], 0, 1, Pagina<ClienteSaldoLinha>.TamanhoPadrao);

    public async Task<IActionResult> OnGetAsync()
    {
        Resultado = await contaCorrente.SaldosAsync(Q, soComSaldo: !Todos, SoVencidos, NumeroPagina);
        if (Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request"))
            return Partial("_ListaSaldos", this);
        return Page();
    }

    public Paginacao Paginacao => Paginacao.De(Resultado,
        p => Url.Page("/ContaCorrente/Index", new { Q, Todos = Todos ? true : (bool?)null, SoVencidos = SoVencidos ? true : (bool?)null, p = p == 1 ? (int?)null : p })!,
        "#listaSaldos");
}
