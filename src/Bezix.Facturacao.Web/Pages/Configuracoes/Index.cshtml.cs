using Microsoft.AspNetCore.Authorization;

namespace Bezix.Facturacao.Web.Pages.Configuracoes;

[Authorize(Policy = Domain.Seguranca.Permissoes.Configuracoes.VerTaxas)]
public class IndexModel : PaginaBase
{
    public void OnGet() { }
}
