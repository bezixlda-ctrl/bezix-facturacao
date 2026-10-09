using System.Security.Claims;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Seguranca;

namespace Bezix.Facturacao.Web.Seguranca;

public sealed class UtilizadorActual(IHttpContextAccessor acessor) : IUtilizadorActual
{
    private ClaimsPrincipal? Principal => acessor.HttpContext?.User;

    public string? Id => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? Email => Principal?.Identity?.IsAuthenticated == true ? Principal.FindFirstValue(ClaimTypes.Email) ?? Principal.Identity.Name : null;
    public string? Ip => acessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool TemPermissao(string permissao) => Principal?.TemPermissao(permissao) == true;
}

public static class ClaimsPrincipalExtensions
{
    public const string ClaimNome = "nome";

    public static bool TemPermissao(this ClaimsPrincipal u, string permissao) =>
        u.HasClaim(Permissoes.ClaimType, permissao);

    public static bool TemAlguma(this ClaimsPrincipal u, params string[] permissoes) =>
        permissoes.Any(u.TemPermissao);

    public static string NomeVisivel(this ClaimsPrincipal u) =>
        u.FindFirstValue(ClaimNome) ?? u.Identity?.Name ?? "";
}
