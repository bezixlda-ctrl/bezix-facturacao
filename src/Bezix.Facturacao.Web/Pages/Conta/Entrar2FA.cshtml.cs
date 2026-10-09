using Bezix.Facturacao.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Conta;

public class Entrar2FAModel(SignInManager<Utilizador> sessoes, UserManager<Utilizador> utilizadores, TimeProvider relogio) : PaginaBase
{
    [BindProperty]
    public string Codigo { get; set; } = "";

    [BindProperty]
    public bool LembrarComputador { get; set; }

    [BindProperty]
    public bool UsarCodigoRecuperacao { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Lembrar { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (await sessoes.GetTwoFactorAuthenticationUserAsync() is null) return RedirectToPage("Entrar");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var utilizador = await sessoes.GetTwoFactorAuthenticationUserAsync();
        if (utilizador is null) return RedirectToPage("Entrar");

        var codigo = (Codigo ?? "").Replace(" ", "").Replace("-", "");
        if (codigo.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Indique o código.");
            return Page();
        }

        var r = UsarCodigoRecuperacao
            ? await sessoes.TwoFactorRecoveryCodeSignInAsync(codigo)
            : await sessoes.TwoFactorAuthenticatorSignInAsync(codigo, Lembrar, LembrarComputador);

        if (r.Succeeded)
        {
            utilizador.UltimoLogin = relogio.GetUtcNow().UtcDateTime;
            await utilizadores.UpdateAsync(utilizador);
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "~/");
        }
        if (r.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Conta bloqueada durante 15 minutos depois de várias tentativas falhadas.");
            return Page();
        }
        ModelState.AddModelError(string.Empty, "Código errado ou expirado. Use o código que aparece agora na aplicação.");
        return Page();
    }
}
