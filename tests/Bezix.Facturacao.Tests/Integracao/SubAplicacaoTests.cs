using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Bezix.Facturacao.Application.Integracoes;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bezix.Facturacao.Tests.Integracao;

/// <summary>
/// Instalação como sub-aplicação (https://bezixcenter.net/facturacao/), ao lado de outros sistemas na raiz do domínio:
/// nenhum link, recurso, formulário ou redireccionamento pode sair do prefixo.
/// </summary>
[Collection(ColeccaoAplicacao.Nome)]
public partial class SubAplicacaoTests(AplicacaoFactory app)
{
    private const string Base = "/facturacao";

    private WebApplicationFactory<Program> ComPrefixo() =>
        app.WithWebHostBuilder(b => b.UseSetting("CaminhoBase", Base));

    private static HttpClient Cliente(WebApplicationFactory<Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

    /// <summary>Caminho de um Location (pode vir absoluto, com o domínio, ou relativo).</summary>
    private static string Caminho(Uri? u) => u is null ? "" : u.IsAbsoluteUri ? u.PathAndQuery : u.OriginalString;

    [GeneratedRegex("""(?:href|src|action|hx-get|hx-post|formaction)="(/[^"]*)""")]
    private static partial Regex Enderecos();

    private static void SoDentroDoPrefixo(string html, string pagina)
    {
        var fora = Enderecos().Matches(html).Select(m => m.Groups[1].Value)
            .Where(u => !u.StartsWith(Base + "/", StringComparison.Ordinal) && u != Base && !u.StartsWith("//", StringComparison.Ordinal))
            .Distinct().ToList();
        Assert.True(fora.Count == 0, $"{pagina}: endereços fora de {Base}: {string.Join(", ", fora)}");
    }

    [Fact]
    public async Task Funciona_dentro_de_uma_pasta_sem_links_para_a_raiz_do_dominio()
    {
        await using var f = ComPrefixo();
        var http = Cliente(f);
        var email = await app.GarantirUtilizadorAsync(PerfisPadrao.Administrador);

        // Sem sessão: vai para o login dentro da pasta.
        var semSessao = await http.GetAsync($"{Base}/Clientes");
        Assert.Equal(HttpStatusCode.Redirect, semSessao.StatusCode);
        Assert.StartsWith($"{Base}/Conta/Entrar", Caminho(semSessao.Headers.Location));

        var login = await http.GetStringAsync($"{Base}/Conta/Entrar");
        SoDentroDoPrefixo(login, "Entrar");
        var token = Regex.Match(login, """name="__RequestVerificationToken" type="hidden" value="([^"]+)""").Groups[1].Value;
        var entrou = await http.PostAsync($"{Base}/Conta/Entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Password"] = AplicacaoFactory.PasswordTeste, ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, entrou.StatusCode);
        Assert.StartsWith(Base, Caminho(entrou.Headers.Location));

        // O cookie de sessão fica limitado à pasta (não é enviado aos outros sistemas do domínio).
        var cookies = entrou.Headers.GetValues("Set-Cookie").Where(c => c.StartsWith("bezix.sessao=", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(cookies);
        Assert.All(cookies, c => Assert.Contains($"path={Base}", c, StringComparison.OrdinalIgnoreCase));

        foreach (var pagina in new[] { "/", "/Documentos", "/Documentos/Editar?tipo=FT", "/Clientes", "/Recibos", "/Relatorios", "/Configuracoes/Empresa",
                     "/Administracao/Integracoes", "/Administracao/Backups", "/Comunicacao/Modelos" })
        {
            var r = await http.GetAsync(Base + pagina);
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{pagina}: {r.StatusCode}");
            SoDentroDoPrefixo(await r.Content.ReadAsStringAsync(), pagina);
        }

        // Recursos estáticos e endpoints do sistema dentro da pasta.
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"{Base}/css/site.css")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"{Base}/lib/phosphor/regular/Phosphor.woff2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"{Base}/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"{Base}/api/openapi/v1.json")).StatusCode);
        var swagger = await http.GetStringAsync($"{Base}/api/docs/index.html");
        Assert.DoesNotContain("\"/api/openapi", swagger);
    }

    [Fact]
    public async Task Api_devolve_location_dentro_da_pasta()
    {
        await using var f = ComPrefixo();
        string chave;
        using (UtilizadorTeste.Como(PerfisPadrao.Administrador))
        using (var scope = app.Services.CreateScope())
            chave = (await scope.ServiceProvider.GetRequiredService<ServicoChavesApi>().CriarAsync("Sub-aplicação", [EscoposApi.ClientesEscrever])).Chave!;

        var http = Cliente(f);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", chave);
        var r = await http.PostAsJsonAsync($"{Base}/api/v1/clientes", new
        {
            nome = $"Cliente pasta {Guid.NewGuid():N}"[..24], tipo = "Empresa", nuit = Random.Shared.Next(400_000_000, 499_999_999).ToString(), aceitarNuitDuplicado = true,
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.StartsWith($"{Base}/api/v1/clientes/", r.Headers.Location?.OriginalString);
    }
}
