using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Microsoft.AspNetCore.Authorization;

namespace Bezix.Facturacao.Web.Pages.Administracao.Utilizadores;

[Authorize(Policy = Permissoes.Administracao.Utilizadores)]
public class IndexModel(ServicoUtilizadores utilizadores) : PaginaBase
{
    public List<UtilizadorResumo> Lista { get; private set; } = [];
    public int AdministradoresActivos { get; private set; }

    public async Task OnGetAsync()
    {
        Lista = await utilizadores.ListarAsync();
        AdministradoresActivos = Lista.Count(u => u.Activo && u.Perfil == PerfisPadrao.Administrador);
    }
}
