using Bezix.Facturacao.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Conta;

public class SairModel(SignInManager<Utilizador> sessoes) : PaginaBase
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await sessoes.SignOutAsync();
        return RedirectToPage("Entrar");
    }
}
