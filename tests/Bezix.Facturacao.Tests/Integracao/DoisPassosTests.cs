using System.Net;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Bezix.Facturacao.Tests.Integracao;

/// <summary>Administradores sem verificação em dois passos só conseguem abrir "A minha conta" (para a activar).</summary>
[Collection(ColeccaoAplicacao.Nome)]
public class DoisPassosTests(AplicacaoFactory app)
{
    [Fact]
    public async Task Administrador_sem_2FA_e_levado_a_activa_lo_e_os_outros_perfis_nao()
    {
        var admin = await app.GarantirUtilizadorAsync(PerfisPadrao.Administrador);
        var gestor = await app.GarantirUtilizadorAsync(PerfisPadrao.Gestor);
        await using var comRegra = app.WithWebHostBuilder(b => b.UseSetting("Seguranca:DoisPassosObrigatorioAdministradores", "true"));
        HttpClient Cliente() => comRegra.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        var http = Cliente();
        await AplicacaoFactory.EntrarAsync(http, admin, AplicacaoFactory.PasswordTeste);
        var painel = await http.GetAsync("/Clientes");
        Assert.Equal(HttpStatusCode.Redirect, painel.StatusCode);
        Assert.Equal("/Conta/Seguranca?obrigatorio=true", painel.Headers.Location?.OriginalString);

        var conta = await http.GetAsync("/Conta/Seguranca?obrigatorio=true");
        Assert.Equal(HttpStatusCode.OK, conta.StatusCode);
        Assert.Contains("Active a verificação em dois passos para continuar", await conta.Content.ReadAsStringAsync());

        // A verificação de saúde continua acessível (é pública).
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);

        var outro = Cliente();
        await AplicacaoFactory.EntrarAsync(outro, gestor, AplicacaoFactory.PasswordTeste);
        Assert.Equal(HttpStatusCode.OK, (await outro.GetAsync("/Clientes")).StatusCode);
    }
}
