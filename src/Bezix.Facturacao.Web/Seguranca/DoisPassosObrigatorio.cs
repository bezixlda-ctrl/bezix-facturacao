using Bezix.Facturacao.Domain.Seguranca;

namespace Bezix.Facturacao.Web.Seguranca;

/// <summary>
/// Quem gere utilizadores (Administrador) tem de ter a verificação em dois passos activa (especificação, secção 12).
/// Sem ela, depois de entrar só consegue abrir "A minha conta" para a activar. Desligável em
/// "Seguranca:DoisPassosObrigatorioAdministradores" (desligado em desenvolvimento e nos testes).
/// </summary>
public sealed class DoisPassosObrigatorio(RequestDelegate seguinte, IConfiguration config)
{
    public const string ClaimDoisPassos = "dois_passos";

    private static readonly string[] Livres = ["/Conta/", "/health", "/api/", "/internal/", "/p/", "/marca/", "/lib/", "/css/", "/js/", "/fonts/", "/favicon"];

    public Task InvokeAsync(HttpContext http)
    {
        var u = http.User;
        if (config.GetValue("Seguranca:DoisPassosObrigatorioAdministradores", true)
            && u.Identity?.IsAuthenticated == true
            && u.Identity.AuthenticationType != Api.AutenticacaoApi.Esquema
            && u.TemPermissao(Permissoes.Administracao.Utilizadores)
            && !u.HasClaim(ClaimDoisPassos, "1")
            && !Livres.Any(p => http.Request.Path.StartsWithSegments(p.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
        {
            http.Response.Redirect("/Conta/Seguranca?obrigatorio=true");
            return Task.CompletedTask;
        }
        return seguinte(http);
    }
}
