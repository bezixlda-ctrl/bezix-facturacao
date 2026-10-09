using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Integracoes;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bezix.Facturacao.Tests.Integracao;

/// <summary>API v1: chaves com escopos, erros em problem+json, Idempotency-Key e limite de pedidos.</summary>
[Collection(ColeccaoAplicacao.Nome)]
public class ApiTests(AplicacaoFactory app)
{
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2));

    private async Task<T> ComoAdminAsync<T>(Func<IServiceProvider, Task<T>> accao)
    {
        using var _ = UtilizadorTeste.Como(PerfisPadrao.Administrador);
        using var scope = app.Services.CreateScope();
        return await accao(scope.ServiceProvider);
    }

    private Task<(int Id, string Chave)> ChaveAsync(params string[] escopos) => ComoAdminAsync(async sp =>
    {
        var nome = "Teste " + Guid.NewGuid().ToString("N")[..8];
        var (r, chave) = await sp.GetRequiredService<ServicoChavesApi>().CriarAsync(nome, escopos);
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var id = await sp.GetRequiredService<AppDbContext>().ChavesApi.Where(c => c.Nome == nome).Select(c => c.Id).SingleAsync();
        return (id, chave!);
    });

    private HttpClient Cliente(string? chave)
    {
        var http = app.CriarCliente();
        if (chave is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", chave);
        return http;
    }

    private Task<(int Cliente, int Serie)> PrepararAsync() => ComoAdminAsync(async sp =>
    {
        var empresas = sp.GetRequiredService<ServicoEmpresa>();
        var e = await empresas.ObterAsync();
        if (e.Nuit == InicializadorBaseDados.NuitPorPreencher)
            await empresas.ActualizarDadosAsync(new DadosEmpresa(e.Nome, "400111222", e.Slogan, "Av. Teste, 1", e.Cidade, e.Provincia, e.Telefones,
                e.Email, e.Site, e.CorPrimaria, e.RodapeTexto, e.PrazoPagamentoDias, e.LimiteNuitObrigatorio, e.ModoArredondamentoIva));
        var cliente = await sp.GetRequiredService<ServicoClientes>().GuardarAsync(null, new DadosCliente(
            $"Cliente API {Guid.NewGuid():N}"[..25], TipoCliente.Empresa, Random.Shared.Next(400_000_000, 499_999_999).ToString(),
            "Rua API", "Maputo", null, null, null, null, null, null, 0, null, null, null, null, true, null), aceitarNuitDuplicado: true);
        var codigo = "P" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var r = await sp.GetRequiredService<ServicoSeries>().CriarAsync(new NovaSerie(TipoDocumento.FT, codigo, Hoje.Year, new DateOnly(Hoje.Year, 1, 1)));
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        return (cliente.Id!.Value, (await sp.GetRequiredService<AppDbContext>().Series.SingleAsync(s => s.Codigo == codigo)).Id);
    });

    private static object Factura(int cliente, int serie, decimal preco, bool emitir = true) => new
    {
        tipo = "FT", clienteId = cliente, serieId = serie, descontoGlobalPct = 0, emitir,
        linhas = new[] { new { descricao = "Licença anual", quantidade = 1, precoUnitario = preco, descontoPct = 0, codigoTaxaIva = "NOR" } },
    };

    private static HttpRequestMessage Post(string url, object corpo, string? idempotencia)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(corpo) };
        if (idempotencia is not null) m.Headers.Add("Idempotency-Key", idempotencia);
        return m;
    }

    [Fact]
    public async Task Sem_chave_ou_com_chave_invalida_responde_401_em_problem_json()
    {
        var sem = await Cliente(null).GetAsync("/api/v1/clientes");
        Assert.Equal(HttpStatusCode.Unauthorized, sem.StatusCode);
        Assert.Equal("application/problem+json", sem.Content.Headers.ContentType?.MediaType);

        var falsa = await Cliente("bzx_00000000_inventada").GetAsync("/api/v1/clientes");
        Assert.Equal(HttpStatusCode.Unauthorized, falsa.StatusCode);
    }

    [Fact]
    public async Task Escopos_limitam_o_que_a_chave_pode_fazer_e_chave_revogada_deixa_de_funcionar()
    {
        var (cliente, serie) = await PrepararAsync();
        var (id, chave) = await ChaveAsync(EscoposApi.ClientesLer);
        var http = Cliente(chave);

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/api/v1/clientes/{cliente}")).StatusCode);
        var proibido = await http.SendAsync(Post("/api/v1/documentos", Factura(cliente, serie, 100), Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, proibido.StatusCode);
        Assert.Equal("application/problem+json", proibido.Content.Headers.ContentType?.MediaType);

        // Rascunho sim, emitir não.
        var (_, soRascunho) = await ChaveAsync(EscoposApi.DocumentosEscrever);
        var emitir = await Cliente(soRascunho).SendAsync(Post("/api/v1/documentos", Factura(cliente, serie, 100), Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, emitir.StatusCode);
        var rascunho = await Cliente(soRascunho).SendAsync(Post("/api/v1/documentos", Factura(cliente, serie, 100, emitir: false), Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Created, rascunho.StatusCode);

        Assert.True(await ComoAdminAsync(async sp => (await sp.GetRequiredService<ServicoChavesApi>().RevogarAsync(id)).Sucesso));
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"/api/v1/clientes/{cliente}")).StatusCode);
    }

    [Fact]
    public async Task Idempotency_key_evita_facturas_duplicadas()
    {
        var (cliente, serie) = await PrepararAsync();
        var (_, chave) = await ChaveAsync(EscoposApi.DocumentosEscrever, EscoposApi.DocumentosEmitir);
        var http = Cliente(chave);

        var semChave = await http.SendAsync(Post("/api/v1/documentos", Factura(cliente, serie, 1_000), null));
        Assert.Equal(HttpStatusCode.BadRequest, semChave.StatusCode);

        var idem = Guid.NewGuid().ToString();
        var primeira = await http.SendAsync(Post("/api/v1/documentos", Factura(cliente, serie, 1_000), idem));
        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        using var doc = JsonDocument.Parse(await primeira.Content.ReadAsStringAsync());
        var id = doc.RootElement.GetProperty("id").GetInt32();
        Assert.Equal(1_160m, doc.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal("Emitido", doc.RootElement.GetProperty("estado").GetString());
        Assert.Equal("MZN", doc.RootElement.GetProperty("moeda").GetString());

        // Repetição (ex.: falha de rede): mesma resposta, nenhuma factura nova.
        var repetida = await http.SendAsync(Post("/api/v1/documentos", Factura(cliente, serie, 1_000), idem));
        Assert.Equal(HttpStatusCode.Created, repetida.StatusCode);
        using (var r = JsonDocument.Parse(await repetida.Content.ReadAsStringAsync()))
            Assert.Equal(id, r.RootElement.GetProperty("id").GetInt32());

        // Mesma chave, outro pedido: recusado.
        var outra = await http.SendAsync(Post("/api/v1/documentos", Factura(cliente, serie, 2_000), idem));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, outra.StatusCode);

        var total = await ComoAdminAsync(sp => sp.GetRequiredService<AppDbContext>().Documentos.CountAsync(d => d.ClienteId == cliente));
        Assert.Equal(1, total);

        // O documento criado lê-se pela API e o PDF existe.
        var (_, leitura) = await ChaveAsync(EscoposApi.DocumentosLer);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(leitura).GetAsync($"/api/v1/documentos/{id}")).StatusCode);
        var pdf = await Cliente(leitura).GetAsync($"/api/v1/documentos/{id}/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Erros_de_validacao_respondem_422_com_a_lista_de_erros()
    {
        var (cliente, serie) = await PrepararAsync();
        var (_, chave) = await ChaveAsync(EscoposApi.DocumentosEscrever);
        var r = await Cliente(chave).SendAsync(Post("/api/v1/documentos",
            new { tipo = "FT", clienteId = cliente, serieId = serie, descontoGlobalPct = 0, linhas = Array.Empty<object>() }, Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("erros").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Mais_de_60_pedidos_por_minuto_com_a_mesma_chave_respondem_429()
    {
        var (_, chave) = await ChaveAsync(EscoposApi.ArtigosLer);
        var http = Cliente(chave);
        for (var i = 0; i < 60; i++)
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/v1/artigos?tamanho=1")).StatusCode);

        var excesso = await http.GetAsync("/api/v1/artigos?tamanho=1");
        Assert.Equal(HttpStatusCode.TooManyRequests, excesso.StatusCode);
        Assert.Equal("60", excesso.Headers.RetryAfter?.ToString());

        // Outra chave não é afectada.
        var (_, outra) = await ChaveAsync(EscoposApi.ArtigosLer);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(outra).GetAsync("/api/v1/artigos?tamanho=1")).StatusCode);
    }

    [Fact]
    public async Task Documentacao_openapi_esta_publicada()
    {
        var r = await Cliente(null).GetAsync("/api/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/documentos", json);
        Assert.Contains("Idempotency-Key", json);
    }
}
