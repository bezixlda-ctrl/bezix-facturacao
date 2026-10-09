using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Microsoft.AspNetCore.Authorization;

namespace Bezix.Facturacao.Web.Pages.Administracao.Perfis;

[Authorize(Policy = Permissoes.Administracao.Utilizadores)]
public class IndexModel(ServicoPerfis perfis) : PaginaBase
{
    public List<PerfilResumo> Lista { get; private set; } = [];

    public async Task OnGetAsync() => Lista = await perfis.ListarAsync();
}
