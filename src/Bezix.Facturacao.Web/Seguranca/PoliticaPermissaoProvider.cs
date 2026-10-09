using System.Security.Claims;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Bezix.Facturacao.Web.Seguranca;

/// <summary>
/// Cada código de permissão (ex.: "Documentos.Anular") é uma policy:
/// [Authorize(Policy = Permissoes.Documentos.Anular)] exige a claim correspondente.
/// </summary>
public sealed class PoliticaPermissaoProvider(IOptions<AuthorizationOptions> opcoes) : DefaultAuthorizationPolicyProvider(opcoes)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string nome)
    {
        if (Permissoes.Existe(nome))
            return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim(Permissoes.ClaimType, nome).Build();
        return await base.GetPolicyAsync(nome);
    }
}

/// <summary>Acrescenta o nome do utilizador às claims (as permissões do perfil já são incluídas pela fábrica base).</summary>
public sealed class FabricaClaims(
    UserManager<Utilizador> utilizadores,
    RoleManager<Perfil> perfis,
    IOptions<IdentityOptions> opcoes)
    : UserClaimsPrincipalFactory<Utilizador, Perfil>(utilizadores, perfis, opcoes)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Utilizador user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim(ClaimsPrincipalExtensions.ClaimNome, user.Nome));

        // Limite de desconto do perfil (o utilizador tem um perfil). Sem claim = sem limite.
        foreach (var nome in await UserManager.GetRolesAsync(user))
        {
            var perfil = await RoleManager.FindByNameAsync(nome);
            if (perfil?.DescontoMaximoPct is { } max)
                id.AddClaim(new Claim(ClaimsPrincipalExtensions.ClaimDescontoMaximo, max.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return id;
    }
}
