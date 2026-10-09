using System.ComponentModel.DataAnnotations;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Infrastructure.Identidade;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Conta;

public class EntrarModel(
    SignInManager<Utilizador> sessoes,
    UserManager<Utilizador> utilizadores,
    IServicoAuditoria auditoria,
    AppDbContext db,
    TimeProvider relogio) : PaginaBase
{
    [BindProperty, Required(ErrorMessage = "Indique o email."), EmailAddress(ErrorMessage = "O email não é válido.")]
    public string Email { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Indique a password."), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty]
    public bool Lembrar { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(UrlSegura());
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var utilizador = await utilizadores.FindByEmailAsync(Email.Trim());
        if (utilizador is { Activo: false })
        {
            await RegistarAsync(utilizador.Id, "Login.Recusado", "Utilizador inactivo");
            ModelState.AddModelError(string.Empty, "Esta conta está desactivada. Fale com um administrador.");
            return Page();
        }

        var r = await sessoes.PasswordSignInAsync(Email.Trim(), Password, Lembrar, lockoutOnFailure: true);
        if (r.Succeeded)
        {
            utilizador!.UltimoLogin = relogio.GetUtcNow().UtcDateTime;
            await utilizadores.UpdateAsync(utilizador);
            await RegistarAsync(utilizador.Id, "Login", null);
            return LocalRedirect(UrlSegura());
        }
        if (r.RequiresTwoFactor)
            return RedirectToPage("Entrar2FA", new { ReturnUrl, Lembrar });

        if (r.IsLockedOut)
        {
            await RegistarAsync(utilizador?.Id, "Login.Bloqueado", null);
            ModelState.AddModelError(string.Empty, "Conta bloqueada durante 15 minutos depois de 5 tentativas falhadas. Tente mais tarde ou peça a um administrador para redefinir a password.");
            return Page();
        }

        await RegistarAsync(utilizador?.Id, "Login.Falhado", null);
        ModelState.AddModelError(string.Empty, "Email ou password errados.");
        return Page();
    }

    private string UrlSegura() => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/";

    private async Task RegistarAsync(string? utilizadorId, string accao, string? detalhe)
    {
        // O utilizador ainda não tem sessão neste pedido: guarda-se o email que tentou entrar.
        auditoria.Registar("Utilizador", utilizadorId, accao, null, detalhe is null ? null : new { detalhe }, utilizador: Email.Trim());
        await db.SaveChangesAsync();
    }
}
