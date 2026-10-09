using System.Net;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Bezix.Facturacao.Tests.Integracao;

[Collection(ColeccaoAplicacao.Nome)]
public class FundacaoTests(AplicacaoFactory app)
{
    [Fact]
    public async Task Ultimo_administrador_activo_nao_pode_ser_desactivado()
    {
        await app.ComScopeAsync(async sp =>
        {
            var gestor = sp.GetRequiredService<UserManager<Utilizador>>();
            var servico = sp.GetRequiredService<ServicoUtilizadores>();
            var admin = await gestor.FindByEmailAsync("admin@teste.local");

            var r = await servico.ActualizarAsync(admin!.Id, new DadosUtilizador(admin.Nome, admin.Email!, PerfisPadrao.Administrador, Activo: false));

            Assert.False(r.Sucesso);
            Assert.Contains("último administrador", r.Erros[0]);
            Assert.True((await gestor.FindByIdAsync(admin.Id))!.Activo);
        });
    }

    [Fact]
    public async Task Administrador_mantem_sempre_todas_as_permissoes()
    {
        await app.ComScopeAsync(async sp =>
        {
            var perfis = sp.GetRequiredService<ServicoPerfis>();
            var admin = (await perfis.ListarAsync()).Single(p => p.Nome == PerfisPadrao.Administrador);

            var r = await perfis.ActualizarAsync(admin.Id, new DadosPerfil(PerfisPadrao.Administrador, null, null, [Permissoes.Clientes.Ver]));

            Assert.False(r.Sucesso);
            Assert.Equal(Permissoes.Todas.Count, (await perfis.ObterAsync(admin.Id))!.Value.Permissoes.Count);
        });
    }

    [Fact]
    public async Task Logotipo_PNG_e_JPG_sao_aceites_e_reduzidos_para_600px()
    {
        await app.ComScopeAsync(async sp =>
        {
            var empresas = sp.GetRequiredService<ServicoEmpresa>();
            var ficheiros = sp.GetRequiredService<Application.Abstracoes.IArmazenamentoFicheiros>();

            foreach (var formato in new[] { SKEncodedImageFormat.Png, SKEncodedImageFormat.Jpeg })
            {
                var r = await empresas.ActualizarLogotipoAsync(Imagem(1200, 400, formato));
                Assert.True(r.Sucesso, string.Join("; ", r.Erros));

                var empresa = await empresas.ObterAsync();
                await using var guardado = ficheiros.Abrir(empresa.LogoPath!);
                using var bitmap = SKBitmap.Decode(guardado);
                Assert.Equal(600, bitmap.Width);
                Assert.Equal(200, bitmap.Height);
            }
        });
    }

    [Fact]
    public async Task Logotipo_noutro_formato_ou_com_extensao_falsa_e_rejeitado()
    {
        await app.ComScopeAsync(async sp =>
        {
            var empresas = sp.GetRequiredService<ServicoEmpresa>();

            var webp = await empresas.ActualizarLogotipoAsync(Imagem(100, 100, SKEncodedImageFormat.Webp));
            var gif = await empresas.ActualizarLogotipoAsync("GIF89a........"u8.ToArray());
            var textoComoPng = await empresas.ActualizarLogotipoAsync("<script>alert(1)</script>"u8.ToArray());
            var grande = await empresas.ActualizarLogotipoAsync(new byte[ServicoEmpresa.TamanhoMaximoLogotipo + 1]);

            Assert.All([webp, gif, textoComoPng], r => Assert.Contains("PNG ou JPG", r.Erros[0]));
            Assert.Contains("2 MB", grande.Erros[0]);
        });
    }

    [Fact]
    public async Task Alteracao_de_configuracao_fica_na_auditoria_com_antes_e_depois()
    {
        await app.ComScopeAsync(async sp =>
        {
            var empresas = sp.GetRequiredService<ServicoEmpresa>();
            var db = sp.GetRequiredService<AppDbContext>();
            var e = await empresas.ObterAsync();

            var r = await empresas.ActualizarDadosAsync(new DadosEmpresa(e.Nome, "400999888", e.Slogan, "Rua de Teste, 1", e.Cidade, e.Provincia,
                e.Telefones, e.Email, e.Site, e.CorPrimaria, e.RodapeTexto, e.PrazoPagamentoDias, e.LimiteNuitObrigatorio, e.ModoArredondamentoIva));

            Assert.True(r.Sucesso);
            var registo = await db.Auditoria.OrderByDescending(a => a.Id).FirstAsync(a => a.Accao == "Configuracao.Alterar");
            Assert.Contains("400999888", registo.Depois);
            Assert.Equal("400999888", (await empresas.ObterAsync()).Nuit);
        });
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("12345678A")]
    [InlineData("")]
    public void NUIT_tem_de_ter_9_algarismos(string nuit) => Assert.False(ServicoEmpresa.NuitValido(nuit));

    [Fact]
    public async Task Health_responde_com_estado_da_base_de_dados_e_ficheiros()
    {
        var r = await app.CriarCliente().GetAsync("/health");
        var corpo = await r.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("baseDados", corpo);
        Assert.Contains("ficheiros", corpo);
    }

    [Fact]
    public async Task Jobs_tick_exige_token()
    {
        var cliente = app.CriarCliente();
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync("/internal/jobs/tick")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync("/internal/jobs/tick?token=errado")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"/internal/jobs/tick?token={AplicacaoFactory.TokenJobs}")).StatusCode);

        var post = new HttpRequestMessage(HttpMethod.Post, "/internal/jobs/tick");
        post.Headers.Add("X-Jobs-Token", AplicacaoFactory.TokenJobs);
        Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(post)).StatusCode);
    }

    private static byte[] Imagem(int largura, int altura, SKEncodedImageFormat formato)
    {
        using var bitmap = new SKBitmap(largura, altura);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.SteelBlue);
        using var imagem = SKImage.FromBitmap(bitmap);
        return imagem.Encode(formato, 90).ToArray();
    }
}
