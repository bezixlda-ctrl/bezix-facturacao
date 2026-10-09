using Bezix.Facturacao.Application.Artigos;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Artigos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Formatacao;
using Bezix.Facturacao.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Artigos;

[Authorize(Policy = Permissoes.Artigos.Ver)]
public class IndexModel(ServicoArtigos artigos, ServicoTaxasIva taxas, TimeProvider relogio) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public FiltroEstado Estado { get; set; } = FiltroEstado.Activos;
    [BindProperty(SupportsGet = true, Name = "p")] public int NumeroPagina { get; set; } = 1;

    public Pagina<Artigo> Resultado { get; private set; } = new([], 0, 1, Pagina<Artigo>.TamanhoPadrao);

    /// <summary>Percentagem em vigor hoje por código de taxa.</summary>
    public Dictionary<string, decimal> TaxasVigentes { get; private set; } = [];
    public bool Filtrado => !string.IsNullOrWhiteSpace(Q) || Estado != FiltroEstado.Activos;

    public async Task<IActionResult> OnGetAsync()
    {
        Resultado = await artigos.PesquisarAsync(new FiltroArtigos(Q, Estado, NumeroPagina));
        TaxasVigentes = (await taxas.VigentesAsync(Formato.HojeEmMaputo(relogio)))
            .GroupBy(t => t.Codigo).ToDictionary(g => g.Key, g => g.First().Percentagem);

        if (Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request"))
            return Partial("_ListaArtigos", this);
        return Page();
    }

    public Paginacao Paginacao => Paginacao.De(Resultado,
        p => Url.Page("/Artigos/Index", new { Q, Estado = Estado == FiltroEstado.Activos ? (FiltroEstado?)null : Estado, p = p == 1 ? (int?)null : p })!,
        "#listaArtigos");
}
