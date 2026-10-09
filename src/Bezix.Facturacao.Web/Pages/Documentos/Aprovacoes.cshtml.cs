using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;

namespace Bezix.Facturacao.Web.Pages.Documentos;

[Authorize(Policy = Permissoes.Documentos.Aprovar)]
public class AprovacoesModel(ServicoEmissao emissao) : PaginaBase
{
    public List<Aprovacao> Pendentes { get; private set; } = [];

    public async Task OnGetAsync() => Pendentes = await emissao.PendentesAsync();
}
