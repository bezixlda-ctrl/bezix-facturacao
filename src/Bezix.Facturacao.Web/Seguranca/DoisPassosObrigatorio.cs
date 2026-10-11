using Bezix.Facturacao.Domain.Seguranca;

namespace Bezix.Facturacao.Web.Seguranca;

/// <summary>
/// Passos de segurança obrigatórios depois de entrar; até estarem feitos só abre "A minha conta":
/// <list type="bullet">
/// <item>mudar a password, quando foi o Administrador a defini-la (conta nova ou password redefinida);</item>
/// <item>activar a verificação em dois passos, para quem gere utilizadores, só se
/// "Seguranca:DoisPassosObrigatorioAdministradores" estiver ligado (desligado por omissão).</item>
/// </list>
/// </summary>
public sealed class DoisPassosObrigatorio(RequestDelegate seguinte, IConfiguration config)
{
    public const string ClaimDoisPassos = "dois_passos";
    public const string ClaimMudarPassword = "mudar_password";

    private static readonly string[] Livres = ["/Conta/", "/health", "/api/", "/internal/", "/p/", "/marca/", "/lib/", "/css/", "/js/", "/fonts/", "/favicon"];

    public Task InvokeAsync(HttpContext http)
    {
        var u = http.User;
        if (u.Identity?.IsAuthenticated != true || u.Identity.AuthenticationType == Api.AutenticacaoApi.Esquema
            || Livres.Any(p => http.Request.Path.StartsWithSegments(p.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            return seguinte(http);

        if (u.HasClaim(ClaimMudarPassword, "1"))
        {
            http.Response.Redirect(http.Request.PathBase + "/Conta/Seguranca?mudarPassword=true");
            return Task.CompletedTask;
        }
        if (config.GetValue("Seguranca:DoisPassosObrigatorioAdministradores", false)
            && u.TemPermissao(Permissoes.Administracao.Utilizadores) && !u.HasClaim(ClaimDoisPassos, "1"))
        {
            http.Response.Redirect(http.Request.PathBase + "/Conta/Seguranca?obrigatorio=true");
            return Task.CompletedTask;
        }
        return seguinte(http);
    }
}
