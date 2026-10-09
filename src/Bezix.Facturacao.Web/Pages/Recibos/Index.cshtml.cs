using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Recibos;

[Authorize(Policy = Permissoes.Documentos.Ver)]
public class IndexModel(ServicoDocumentos documentos) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? De { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Ate { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int NumeroPagina { get; set; } = 1;

    public Pagina<DocumentoResumo> Resultado { get; private set; } = new([], 0, 1, Pagina<DocumentoResumo>.TamanhoPadrao);

    public async Task<IActionResult> OnGetAsync()
    {
        Resultado = await documentos.PesquisarAsync(new FiltroDocumentos(TipoDocumento.RC, null, null, Q, De, Ate, NumeroPagina));
        if (Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request"))
            return Partial("_ListaRecibos", this);
        return Page();
    }

    public Paginacao Paginacao => Paginacao.De(Resultado,
        p => Url.Page("/Recibos/Index", new { Q, De, Ate, p = p == 1 ? (int?)null : p })!, "#listaRecibos");
}
