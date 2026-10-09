using System.Net;
using System.Text.RegularExpressions;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bezix.Facturacao.Tests.Integracao;

/// <summary>
/// Arranca a aplicação real contra uma base de dados SQL Server LocalDB temporária
/// (o comportamento de bloqueios e numeração só se testa a sério em SQL Server).
/// </summary>
public sealed partial class AplicacaoFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TokenJobs = "token-de-teste-jobs";
    public const string PasswordTeste = "Teste12345Aa";

    private readonly string _baseDados = $"BezixFacturacao_Testes_{Guid.NewGuid():N}";
    private readonly string _pastaFicheiros = Path.Combine(Path.GetTempPath(), "bezix-testes", Guid.NewGuid().ToString("N"));

    public string Ligacao => $@"Server=(localdb)\MSSQLLocalDB;Database={_baseDados};Trusted_Connection=True;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("ConnectionStrings:BaseDados", Ligacao);
        builder.UseSetting("Registos:TabelaBaseDados", "false");
        builder.UseSetting("Ficheiros:Raiz", _pastaFicheiros);
        builder.UseSetting("Jobs:Token", TokenJobs);
        builder.UseSetting("Inicializacao:AdministradorEmail", "admin@teste.local");
        builder.UseSetting("Inicializacao:AdministradorNome", "Admin Teste");
        builder.UseSetting("Inicializacao:AdministradorPassword", PasswordTeste);
    }

    public Task InitializeAsync()
    {
        // Força o arranque (migrações + dados iniciais).
        _ = Services;
        return Task.CompletedTask;
    }

    public new async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
        try { Directory.Delete(_pastaFicheiros, recursive: true); } catch (IOException) { }
    }

    public HttpClient CriarCliente() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });

    /// <summary>Cria (uma vez) um utilizador com o perfil indicado e devolve o email.</summary>
    public async Task<string> GarantirUtilizadorAsync(string perfil)
    {
        var email = $"{perfil.ToLowerInvariant()}@teste.local";
        using var scope = Services.CreateScope();
        var gestor = scope.ServiceProvider.GetRequiredService<UserManager<Utilizador>>();
        if (await gestor.FindByEmailAsync(email) is not null) return email;

        var u = new Utilizador { UserName = email, Email = email, EmailConfirmed = true, Nome = $"Teste {perfil}", CreatedAt = DateTime.UtcNow };
        var r = await gestor.CreateAsync(u, PasswordTeste);
        Assert.True(r.Succeeded, string.Join("; ", r.Errors.Select(e => e.Description)));
        await gestor.AddToRoleAsync(u, perfil);
        return email;
    }

    public async Task<HttpClient> ClienteComSessaoAsync(string perfil)
    {
        var email = perfil == PerfisPadrao.Administrador ? "admin@teste.local" : await GarantirUtilizadorAsync(perfil);
        var cliente = CriarCliente();
        var r = await EntrarAsync(cliente, email, PasswordTeste);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return cliente;
    }

    public static async Task<HttpResponseMessage> EntrarAsync(HttpClient cliente, string email, string password)
    {
        var token = await TokenAntiforgeryAsync(cliente, "/Conta/Entrar");
        return await cliente.PostAsync("/Conta/Entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
        }));
    }

    public static async Task<string> TokenAntiforgeryAsync(HttpClient cliente, string url)
    {
        var html = await cliente.GetStringAsync(url);
        var m = TokenRegex().Match(html);
        Assert.True(m.Success, $"Sem token antiforgery em {url}");
        return m.Groups[1].Value;
    }

    public async Task ComScopeAsync(Func<IServiceProvider, Task> accao)
    {
        using var scope = Services.CreateScope();
        await accao(scope.ServiceProvider);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}

[CollectionDefinition(Nome)]
public sealed class ColeccaoAplicacao : ICollectionFixture<AplicacaoFactory>
{
    public const string Nome = "Aplicação";
}
