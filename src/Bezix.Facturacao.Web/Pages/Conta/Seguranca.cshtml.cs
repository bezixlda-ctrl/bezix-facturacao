using System.Text.Encodings.Web;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Infrastructure.Identidade;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace Bezix.Facturacao.Web.Pages.Conta;

public class SegurancaModel(
    UserManager<Utilizador> utilizadores,
    SignInManager<Utilizador> sessoes,
    IServicoAuditoria auditoria,
    AppDbContext db,
    Application.Configuracao.ServicoEmpresa empresas) : PaginaBase
{
    public bool DoisPassosActivo { get; private set; }
    public string? ChaveManual { get; private set; }
    public string? QrSvg { get; private set; }
    public int CodigosRecuperacaoRestantes { get; private set; }

    [TempData]
    public string? CodigosRecuperacaoNovos { get; set; }

    [BindProperty] public string PasswordActual { get; set; } = "";
    [BindProperty] public string PasswordNova { get; set; } = "";
    [BindProperty] public string PasswordConfirmacao { get; set; } = "";
    [BindProperty] public string CodigoVerificacao { get; set; } = "";
    [BindProperty] public string? Assinatura { get; set; }

    [BindProperty(SupportsGet = true)] public bool Obrigatorio { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // Sessão aberta antes de o 2FA ser obrigatório: actualiza as claims em vez de pedir para sair e voltar a entrar.
        if (await utilizadores.GetUserAsync(User) is { TwoFactorEnabled: true } u && !User.HasClaim(Seguranca.DoisPassosObrigatorio.ClaimDoisPassos, "1"))
        {
            await sessoes.RefreshSignInAsync(u);
            if (Obrigatorio) return LocalRedirect("~/");
        }
        await CarregarAsync();
        return Page();
    }

    /// <summary>Assinatura pessoal nos emails a clientes (variável {assinatura}).</summary>
    public async Task<IActionResult> OnPostAssinaturaAsync()
    {
        var u = await utilizadores.GetUserAsync(User);
        if (u is null) return Challenge();
        var texto = string.IsNullOrWhiteSpace(Assinatura) ? null : Assinatura.Replace("\r\n", "\n").Trim();
        if (texto?.Length > 2000) ModelState.AddModelError(string.Empty, "A assinatura pode ter no máximo 2000 caracteres.");
        else
        {
            u.AssinaturaEmail = texto;
            await utilizadores.UpdateAsync(u);
            return RedirecionarComSucesso("Assinatura guardada.");
        }
        await CarregarAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var u = await utilizadores.GetUserAsync(User);
        if (u is null) return Challenge();

        if (PasswordNova != PasswordConfirmacao)
            ModelState.AddModelError(string.Empty, "A nova password e a confirmação são diferentes.");
        else
        {
            var r = await utilizadores.ChangePasswordAsync(u, PasswordActual, PasswordNova);
            foreach (var e in r.Errors) ModelState.AddModelError(string.Empty, e.Description);
            if (r.Succeeded)
            {
                auditoria.Registar("Utilizador", u.Id, "Utilizador.AlterarPassword");
                await db.SaveChangesAsync();
                await sessoes.RefreshSignInAsync(u);
                return RedirecionarComSucesso("Password alterada.");
            }
        }
        await CarregarAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostActivarAsync()
    {
        var u = await utilizadores.GetUserAsync(User);
        if (u is null) return Challenge();

        var codigo = CodigoVerificacao.Replace(" ", "").Replace("-", "");
        var valido = await utilizadores.VerifyTwoFactorTokenAsync(u, utilizadores.Options.Tokens.AuthenticatorTokenProvider, codigo);
        if (!valido)
        {
            ModelState.AddModelError(string.Empty, "Código errado. Confirme que a hora do telemóvel está certa e use o código que aparece agora.");
            await CarregarAsync();
            return Page();
        }

        await utilizadores.SetTwoFactorEnabledAsync(u, true);
        var codigos = await utilizadores.GenerateNewTwoFactorRecoveryCodesAsync(u, 10);
        CodigosRecuperacaoNovos = string.Join(" ", codigos ?? []);
        auditoria.Registar("Utilizador", u.Id, "Utilizador.Activar2FA");
        await db.SaveChangesAsync();
        await sessoes.RefreshSignInAsync(u); // a sessão passa a ter a marca "dois passos" (administradores deixam de ser redireccionados)
        return RedirecionarComSucesso("Verificação em dois passos activada.");
    }

    public async Task<IActionResult> OnPostDesactivarAsync()
    {
        var u = await utilizadores.GetUserAsync(User);
        if (u is null) return Challenge();
        await utilizadores.SetTwoFactorEnabledAsync(u, false);
        await utilizadores.ResetAuthenticatorKeyAsync(u);
        auditoria.Registar("Utilizador", u.Id, "Utilizador.Desactivar2FA");
        await db.SaveChangesAsync();
        await sessoes.RefreshSignInAsync(u);
        return RedirecionarComSucesso("Verificação em dois passos desactivada.");
    }

    public async Task<IActionResult> OnPostNovosCodigosAsync()
    {
        var u = await utilizadores.GetUserAsync(User);
        if (u is null) return Challenge();
        var codigos = await utilizadores.GenerateNewTwoFactorRecoveryCodesAsync(u, 10);
        CodigosRecuperacaoNovos = string.Join(" ", codigos ?? []);
        auditoria.Registar("Utilizador", u.Id, "Utilizador.NovosCodigosRecuperacao");
        await db.SaveChangesAsync();
        return RedirecionarComSucesso("Novos códigos de recuperação criados. Os anteriores deixaram de funcionar.");
    }

    private async Task CarregarAsync()
    {
        var u = await utilizadores.GetUserAsync(User) ?? throw new InvalidOperationException("Sem sessão.");
        DoisPassosActivo = u.TwoFactorEnabled;
        Assinatura ??= u.AssinaturaEmail;
        if (DoisPassosActivo)
        {
            CodigosRecuperacaoRestantes = await utilizadores.CountRecoveryCodesAsync(u);
            return;
        }

        var chave = await utilizadores.GetAuthenticatorKeyAsync(u);
        if (string.IsNullOrEmpty(chave))
        {
            await utilizadores.ResetAuthenticatorKeyAsync(u);
            chave = await utilizadores.GetAuthenticatorKeyAsync(u);
        }
        ChaveManual = string.Join(" ", Enumerable.Range(0, (chave!.Length + 3) / 4).Select(i => chave.Substring(i * 4, Math.Min(4, chave.Length - i * 4)))).ToLowerInvariant();

        var emissor = UrlEncoder.Default.Encode((await empresas.ObterAsync()).Nome + " Facturação");
        var uri = $"otpauth://totp/{emissor}:{UrlEncoder.Default.Encode(u.Email!)}?secret={chave}&issuer={emissor}&digits=6";
        using var gerador = new QRCodeGenerator();
        using var dados = gerador.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        QrSvg = new SvgQRCode(dados).GetGraphic(4);
    }
}
