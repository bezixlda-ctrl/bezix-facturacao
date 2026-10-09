using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Clientes;

[Authorize(Policy = Permissoes.Clientes.Ver)]
public class IndexModel(ServicoClientes clientes) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Etiqueta { get; set; }
    [BindProperty(SupportsGet = true)] public FiltroEstado Estado { get; set; } = FiltroEstado.Activos;
    [BindProperty(SupportsGet = true, Name = "p")] public int NumeroPagina { get; set; } = 1;

    public Pagina<ClienteLinha> Resultado { get; private set; } = new([], 0, 1, Pagina<ClienteLinha>.TamanhoPadrao);
    public List<string> Etiquetas { get; private set; } = [];
    public bool SoProprios { get; private set; }
    public bool Filtrado => !string.IsNullOrWhiteSpace(Q) || !string.IsNullOrWhiteSpace(Etiqueta) || Estado != FiltroEstado.Activos;

    public async Task<IActionResult> OnGetAsync()
    {
        Resultado = await clientes.PesquisarAsync(new FiltroClientes(Q, Etiqueta, Estado, NumeroPagina));
        SoProprios = await clientes.SoProprioAsync();

        // Pesquisa ao vivo (htmx): devolve só a tabela.
        if (Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request"))
            return Partial("_ListaClientes", this);

        Etiquetas = await clientes.EtiquetasAsync();
        return Page();
    }

    public Paginacao Paginacao => Paginacao.De(Resultado,
        p => Url.Page("/Clientes/Index", new { Q, Etiqueta, Estado = Estado == FiltroEstado.Activos ? (FiltroEstado?)null : Estado, p = p == 1 ? (int?)null : p })!,
        "#listaClientes");
}
