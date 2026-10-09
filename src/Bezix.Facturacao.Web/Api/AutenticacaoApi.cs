using System.Security.Claims;
using System.Text.Encodings.Web;
using Bezix.Facturacao.Application.Integracoes;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Bezix.Facturacao.Web.Api;

/// <summary>
/// Autenticação da API v1: "Authorization: Bearer bzx_…". A chave dá claims de permissão (as mesmas da interface,
/// a partir dos escopos) e claims "escopo" para as policies de cada endpoint.
/// </summary>
public sealed class AutenticacaoApi(IOptionsMonitor<AuthenticationSchemeOptions> opcoes, ILoggerFactory logs, UrlEncoder codificador, ServicoChavesApi chaves)
    : AuthenticationHandler<AuthenticationSchemeOptions>(opcoes, logs, codificador)
{
    public const string Esquema = "ChaveApi";
    public const string ClaimEscopo = "escopo";
    public const string ClaimChaveId = "chave_api_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cabecalho = Request.Headers.Authorization.ToString();
        if (!cabecalho.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();

        var chave = await chaves.AutenticarAsync(cabecalho["Bearer ".Length..].Trim(), Context.RequestAborted);
        if (chave is null) return AuthenticateResult.Fail("Chave de API inválida ou revogada.");

        var escopos = chave.ListaEscopos();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"api:{chave.Id}"),
            new(ClaimTypes.Name, $"api:{chave.Nome}"),
            new(ClaimTypes.Email, $"api:{chave.Nome}"),
            new(ClaimsPrincipalExtensions.ClaimNome, $"API · {chave.Nome}"),
            new(ClaimChaveId, chave.Id.ToString()),
        };
        claims.AddRange(escopos.Select(e => new Claim(ClaimEscopo, e)));
        claims.AddRange(EscoposApi.PermissoesDe(escopos).Select(p => new Claim(Permissoes.ClaimType, p)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Esquema));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Esquema));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        await Results.Problem(statusCode: 401, title: "Não autenticado",
            detail: "Envie a chave da API no cabeçalho Authorization: Bearer <chave>.").ExecuteAsync(Context);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Results.Problem(statusCode: 403, title: "Sem permissão",
            detail: "A chave da API não tem o escopo necessário para esta operação.").ExecuteAsync(Context);
    }
}
