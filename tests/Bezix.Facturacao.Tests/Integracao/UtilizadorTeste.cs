using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Http;

namespace Bezix.Facturacao.Tests.Integracao;

/// <summary>
/// Nos pedidos HTTP usa o utilizador real da sessão; fora deles (testes de serviços) usa o utilizador
/// definido com <see cref="Como"/>, com as permissões do perfil padrão.
/// </summary>
public sealed class UtilizadorTeste(IHttpContextAccessor acessor) : IUtilizadorActual
{
    public sealed record Ficticio(string Id, string Email, string Nome, IReadOnlySet<string> Permissoes, decimal? DescontoMaximoPct);

    private static readonly AsyncLocal<Ficticio?> Actual = new();
    private readonly UtilizadorActual _real = new(acessor);

    public static IDisposable Como(string perfil, string? id = null)
    {
        var def = PerfisPadrao.Todos.Single(p => p.Nome == perfil);
        var anterior = Actual.Value;
        Actual.Value = new Ficticio(id ?? $"teste-{perfil.ToLowerInvariant()}", $"{perfil.ToLowerInvariant()}@teste.local", $"Teste {perfil}",
            def.Permissoes.ToHashSet(), def.DescontoMaximoPct);
        return new Restaurar(() => Actual.Value = anterior);
    }

    private bool Http => acessor.HttpContext is not null;

    public string? Id => Http ? _real.Id : Actual.Value?.Id;
    public string? Email => Http ? _real.Email : Actual.Value?.Email;
    public string? Ip => Http ? _real.Ip : "127.0.0.1";
    public string? Nome => Http ? _real.Nome : Actual.Value?.Nome;
    public decimal? DescontoMaximoPct => Http ? _real.DescontoMaximoPct : Actual.Value?.DescontoMaximoPct;
    public bool TemPermissao(string permissao) => Http ? _real.TemPermissao(permissao) : Actual.Value?.Permissoes.Contains(permissao) == true;

    private sealed class Restaurar(Action accao) : IDisposable
    {
        public void Dispose() => accao();
    }
}
