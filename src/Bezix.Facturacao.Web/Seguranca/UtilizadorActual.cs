using System.Security.Claims;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Seguranca;

namespace Bezix.Facturacao.Web.Seguranca;

public sealed class UtilizadorActual(IHttpContextAccessor acessor, IContextoSistema? sistema = null) : IUtilizadorActual
{
    private ClaimsPrincipal? Principal => acessor.HttpContext?.User;
    private bool Sistema => sistema?.Activo == true;

    public string? Id => Sistema ? null : Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? Email => Sistema ? "sistema" : Principal?.Identity?.IsAuthenticated == true ? Principal.FindFirstValue(ClaimTypes.Email) ?? Principal.Identity.Name : null;
    public string? Ip => acessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? Nome => Sistema ? "Sistema" : Principal?.Identity?.IsAuthenticated == true ? Principal.NomeVisivel() : null;

    public decimal? DescontoMaximoPct => Sistema ? null :
        decimal.TryParse(Principal?.FindFirstValue(ClaimsPrincipalExtensions.ClaimDescontoMaximo), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Em nome do sistema (tarefas automáticas) tem todas as permissões; fica registado como "sistema" na auditoria.</summary>
    public bool TemPermissao(string permissao) => Sistema || Principal?.TemPermissao(permissao) == true;
}

/// <summary>Endereço do pedido actual (https://servidor), usado nos links quando não há "URL pública" configurada.</summary>
public sealed class EnderecoPublico(IHttpContextAccessor acessor) : IEnderecoPublico
{
    public string? Base => acessor.HttpContext?.Request is { } r ? $"{r.Scheme}://{r.Host}{r.PathBase}" : null;
}

public static class ClaimsPrincipalExtensions
{
    public const string ClaimNome = "nome";
    public const string ClaimDescontoMaximo = "desconto_max";

    public static bool TemPermissao(this ClaimsPrincipal u, string permissao) =>
        u.HasClaim(Permissoes.ClaimType, permissao);

    public static bool TemAlguma(this ClaimsPrincipal u, params string[] permissoes) =>
        permissoes.Any(u.TemPermissao);

    public static string NomeVisivel(this ClaimsPrincipal u) =>
        u.FindFirstValue(ClaimNome) ?? u.Identity?.Name ?? "";
}
