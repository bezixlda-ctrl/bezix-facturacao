using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;

namespace Bezix.Facturacao.Web.Pages.Relatorios;

[Authorize(Policy = Permissoes.Relatorios.Vendas)]
public class IndexModel : PaginaBase
{
    public void OnGet() { }
}
