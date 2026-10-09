using Bezix.Facturacao.Application.Artigos;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Domain.Artigos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages;

/// <summary>Pesquisa global da barra superior (Ctrl+K). Devolve só o fragmento com os resultados.</summary>
public class PesquisaModel(ServicoClientes clientes, ServicoArtigos artigos) : PaginaBase
{
    public const int Limite = 6;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public IReadOnlyList<ClienteLinha> Clientes { get; private set; } = [];
    public IReadOnlyList<Artigo> Artigos { get; private set; } = [];
    public bool Pesquisou => (Q?.Trim().Length ?? 0) >= 2;

    public async Task<IActionResult> OnGetAsync()
    {
        if (Pesquisou)
        {
            if (User.TemPermissao(Permissoes.Clientes.Ver))
                Clientes = (await clientes.PesquisarAsync(new FiltroClientes(Q, Tamanho: Limite))).Itens;
            if (User.TemPermissao(Permissoes.Artigos.Ver))
                Artigos = (await artigos.PesquisarAsync(new FiltroArtigos(Q, Tamanho: Limite))).Itens;
        }
        return Partial("_ResultadosPesquisa", this);
    }
}
