using System.Net;
using System.Text.RegularExpressions;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Microsoft.Extensions.DependencyInjection;

namespace Bezix.Facturacao.Tests.Integracao;

/// <summary>
/// Quem recebe uma password do Administrador (conta nova ou password redefinida) tem de a mudar no primeiro acesso;
/// até lá só abre "A minha conta". O Administrador inicial (configuração) não passa por isto.
/// </summary>
[Collection(ColeccaoAplicacao.Nome)]
public class MudarPasswordTests(AplicacaoFactory app)
{
    private const string Inicial = "Inicial12345";

    private static string Caminho(Uri? u) => u is null ? "" : u.IsAbsoluteUri ? u.PathAndQuery : u.OriginalString;

    private async Task<HttpResponseMessage> MudarAsync(HttpClient http, string actual, string nova)
    {
        var token = await AplicacaoFactory.TokenAntiforgeryAsync(http, "/Conta/Seguranca");
        return await http.PostAsync("/Conta/Seguranca?handler=Password", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PasswordActual"] = actual, ["PasswordNova"] = nova, ["PasswordConfirmacao"] = nova, ["__RequestVerificationToken"] = token,
        }));
    }

    [Fact]
    public async Task Utilizador_criado_pelo_administrador_muda_a_password_no_primeiro_acesso()
    {
        var email = $"novo.{Guid.NewGuid():N}"[..14] + "@teste.local";
        var r = await app.ComScopeAsync(sp => sp.GetRequiredService<ServicoUtilizadores>()
            .CriarAsync(new DadosUtilizador("Utilizador Novo", email, PerfisPadrao.Facturador, Activo: true), Inicial));
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));

        var http = app.CriarCliente();
        await AplicacaoFactory.EntrarAsync(http, email, Inicial);

        // Qualquer página leva a "A minha conta" com o aviso.
        var bloqueado = await http.GetAsync("/Clientes");
        Assert.Equal(HttpStatusCode.Redirect, bloqueado.StatusCode);
        Assert.Equal("/Conta/Seguranca?mudarPassword=true", Caminho(bloqueado.Headers.Location));
        Assert.Contains("Defina a sua password para continuar", await http.GetStringAsync("/Conta/Seguranca?mudarPassword=true"));

        var mudou = await MudarAsync(http, Inicial, "MinhaPassword2026");
        Assert.Equal(HttpStatusCode.Redirect, mudou.StatusCode);
        Assert.Equal("/", Caminho(mudou.Headers.Location));
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/Clientes")).StatusCode);

        // O Administrador redefine a password: volta a ser obrigatório mudá-la.
        var id = await app.ComScopeAsync(async sp => (await sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Utilizador>>().FindByEmailAsync(email))!.Id);
        Assert.True((await app.ComScopeAsync(sp => sp.GetRequiredService<ServicoUtilizadores>().RedefinirPasswordAsync(id, "Redefinida2026"))).Sucesso);

        var outra = app.CriarCliente();
        await AplicacaoFactory.EntrarAsync(outra, email, "Redefinida2026");
        Assert.Equal("/Conta/Seguranca?mudarPassword=true", Caminho((await outra.GetAsync("/Documentos")).Headers.Location));
    }

    [Fact]
    public async Task Administrador_inicial_e_contas_normais_nao_sao_obrigados_nem_a_mudar_password_nem_a_dois_passos()
    {
        var http = app.CriarCliente();
        await AplicacaoFactory.EntrarAsync(http, "admin@teste.local", AplicacaoFactory.PasswordTeste);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/Clientes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/Administracao/Utilizadores")).StatusCode);
        Assert.DoesNotMatch(new Regex("Defina a sua password|Active a verificação em dois passos para continuar"), await http.GetStringAsync("/Conta/Seguranca"));
    }
}
