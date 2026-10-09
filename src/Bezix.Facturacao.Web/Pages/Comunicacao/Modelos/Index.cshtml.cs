using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;

namespace Bezix.Facturacao.Web.Pages.Comunicacao.Modelos;

[Authorize(Policy = Permissoes.Comunicacao.GerirModelos)]
public class IndexModel(ServicoComunicacao comunicacao) : PaginaBase
{
    public List<ModeloComunicacao> Modelos { get; private set; } = [];

    public async Task OnGetAsync() => Modelos = await comunicacao.ModelosAsync();

    public static string Grupo(string codigo) => codigo switch
    {
        _ when codigo.StartsWith("documento.") => "Envio de documentos",
        _ when codigo.StartsWith("lembrete.") => "Lembretes de pagamento",
        _ => "Outros",
    };
}
