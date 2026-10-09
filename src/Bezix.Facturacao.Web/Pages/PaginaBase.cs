using Bezix.Facturacao.Application.Comum;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Bezix.Facturacao.Web.Pages;

public abstract class PaginaBase : PageModel
{
    public const string ChaveSucesso = "Sucesso";
    public const string ChaveErro = "Erro";

    protected void Sucesso(string mensagem) => TempData[ChaveSucesso] = mensagem;
    protected void Erro(string mensagem) => TempData[ChaveErro] = mensagem;

    /// <summary>Passa os erros do caso de uso para o ModelState (mostrados no resumo do formulário).</summary>
    protected void AdicionarErros(Resultado resultado)
    {
        foreach (var e in resultado.Erros) ModelState.AddModelError(string.Empty, e);
    }

    protected IActionResult RedirecionarComSucesso(string mensagem, string? pagina = null, object? rota = null)
    {
        Sucesso(mensagem);
        return pagina is null ? RedirectToPage(rota) : RedirectToPage(pagina, rota);
    }
}
