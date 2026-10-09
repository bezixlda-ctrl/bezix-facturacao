using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Configuracoes.ContasBancarias;

[Authorize(Policy = Permissoes.Configuracoes.Gerir)]
public class IndexModel(ServicoContasBancarias contas) : PaginaBase
{
    public List<ContaBancaria> Contas { get; private set; } = [];

    public async Task OnGetAsync() => Contas = await contas.ListarAsync();

    public async Task<IActionResult> OnPostRemoverAsync(int id)
    {
        await contas.RemoverAsync(id);
        return RedirecionarComSucesso("Conta bancária removida.");
    }
}
