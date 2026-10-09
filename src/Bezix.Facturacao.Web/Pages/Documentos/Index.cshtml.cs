using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Documentos;

[Authorize(Policy = Permissoes.Documentos.Ver)]
public class IndexModel(ServicoDocumentos documentos, Application.Recebimentos.ServicoContaCorrente contaCorrente) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public TipoDocumento? Tipo { get; set; }
    [BindProperty(SupportsGet = true)] public EstadoDocumento? Estado { get; set; }
    [BindProperty(SupportsGet = true)] public int? ClienteId { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? De { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Ate { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int NumeroPagina { get; set; } = 1;

    public Pagina<DocumentoResumo> Resultado { get; private set; } = new([], 0, 1, Pagina<DocumentoResumo>.TamanhoPadrao);
    public IReadOnlyList<TipoDocumento> TiposNovos { get; private set; } = [];
    public Dictionary<int, Application.Recebimentos.SituacaoDocumento> Situacao { get; private set; } = [];
    public bool Filtrado => !string.IsNullOrWhiteSpace(Q) || Tipo is not null || Estado is not null || ClienteId is not null || De is not null || Ate is not null;

    public async Task<IActionResult> OnGetAsync()
    {
        Resultado = await documentos.PesquisarAsync(new FiltroDocumentos(Tipo, Estado, ClienteId, Q, De, Ate, NumeroPagina));
        Situacao = await contaCorrente.SituacaoAsync(Resultado.Itens.Select(d => d.Id).ToList());
        TiposNovos = documentos.TiposEditaveis().Where(t => !RegrasTipo.ExigeOrigem(t)).ToList();
        if (Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request"))
            return Partial("_ListaDocumentos", this);
        return Page();
    }

    public Paginacao Paginacao => Paginacao.De(Resultado,
        p => Url.Page("/Documentos/Index", new { Q, Tipo, Estado, ClienteId, De, Ate, p = p == 1 ? (int?)null : p })!,
        "#listaDocumentos");
}
