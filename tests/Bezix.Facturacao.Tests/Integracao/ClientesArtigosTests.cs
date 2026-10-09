using System.Net;
using System.Text;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Artigos;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Importacao;
using Bezix.Facturacao.Domain.Artigos;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bezix.Facturacao.Tests.Integracao;

[Collection(ColeccaoAplicacao.Nome)]
public class ClientesArtigosTests(AplicacaoFactory app)
{
    private static readonly Random Aleatorio = new();

    /// <summary>NUIT aleatório (9 algarismos) para os testes não colidirem na base de dados partilhada.</summary>
    private static string NovoNuit() => Aleatorio.Next(100_000_000, 999_999_999).ToString();

    private static DadosCliente Cliente(string nome, string? nuit = null, string? telefone = null, string? responsavel = null) =>
        new(nome, TipoCliente.Empresa, nuit, null, "Maputo", telefone, null, null, null, null, null, 0, null, null, null, null, true, responsavel);

    [Fact]
    public async Task NUIT_duplicado_pede_confirmacao_e_so_grava_depois_de_confirmado()
    {
        await app.ComScopeAsync(async sp =>
        {
            var clientes = sp.GetRequiredService<ServicoClientes>();
            var nuit = NovoNuit();

            var primeiro = await clientes.GuardarAsync(null, Cliente("Empresa Original", nuit));
            var segundo = await clientes.GuardarAsync(null, Cliente("Empresa Filial", nuit));

            Assert.True(primeiro.Resultado.Sucesso);
            Assert.True(segundo.PrecisaConfirmacao);
            Assert.Equal("Empresa Original", Assert.Single(segundo.Duplicados).Nome);
            Assert.Single(await clientes.ProcurarPorNuitAsync(nuit));

            var confirmado = await clientes.GuardarAsync(null, Cliente("Empresa Filial", nuit), aceitarNuitDuplicado: true);
            Assert.True(confirmado.Resultado.Sucesso);
            Assert.Equal(2, (await clientes.ProcurarPorNuitAsync(nuit)).Count);
        });
    }

    [Fact]
    public async Task Editar_o_proprio_cliente_nao_conta_como_NUIT_duplicado()
    {
        await app.ComScopeAsync(async sp =>
        {
            var clientes = sp.GetRequiredService<ServicoClientes>();
            var nuit = NovoNuit();
            var criado = await clientes.GuardarAsync(null, Cliente("Cliente Editável", nuit));

            var editado = await clientes.GuardarAsync(criado.Id, Cliente("Cliente Editável, Lda", nuit));

            Assert.True(editado.Resultado.Sucesso);
            Assert.Equal("Cliente Editável, Lda", (await clientes.ObterAsync(criado.Id!.Value))!.Nome);
        });
    }

    [Fact]
    public async Task Pesquisa_ignora_acentos_e_encontra_por_NUIT_e_telefone()
    {
        await app.ComScopeAsync(async sp =>
        {
            var clientes = sp.GetRequiredService<ServicoClientes>();
            var nuit = NovoNuit();
            var marca = Guid.NewGuid().ToString("N")[..6];
            await clientes.GuardarAsync(null, Cliente($"Conceição Comércio {marca}", nuit, "+258 84 123 4567"));

            async Task<int> Contar(string q) => (await clientes.PesquisarAsync(new FiltroClientes(q))).Itens.Count(c => c.Nuit == nuit);

            Assert.Equal(1, await Contar($"conceicao comercio {marca}"));
            Assert.Equal(1, await Contar(nuit));
            Assert.Equal(1, await Contar(nuit[..5]));
            Assert.Equal(1, await Contar("841234567"));
        });
    }

    [Fact]
    public async Task Com_so_os_proprios_activo_o_facturador_ve_apenas_os_seus_clientes()
    {
        var emailFacturador = await app.GarantirUtilizadorAsync(PerfisPadrao.Facturador);
        string idFacturador = "", idCliente = "";
        var nomeAlheio = $"Cliente de outro {Guid.NewGuid():N}";
        var nomeProprio = $"Cliente do facturador {Guid.NewGuid():N}";

        await app.ComScopeAsync(async sp =>
        {
            idFacturador = (await sp.GetRequiredService<UserManager<Utilizador>>().FindByEmailAsync(emailFacturador))!.Id;
            var clientes = sp.GetRequiredService<ServicoClientes>();
            await clientes.GuardarAsync(null, Cliente(nomeAlheio, responsavel: "outro-utilizador"));
            idCliente = (await clientes.GuardarAsync(null, Cliente(nomeProprio, responsavel: idFacturador))).Id!.ToString()!;
            await DefinirSoPropriosAsync(sp, true);
        });

        try
        {
            var facturador = await app.ClienteComSessaoAsync(PerfisPadrao.Facturador);
            var gestor = await app.ClienteComSessaoAsync(PerfisPadrao.Gestor);

            var listaFacturador = await facturador.GetStringAsync("/Clientes?Q=" + Uri.EscapeDataString("Cliente d"));
            Assert.Contains(nomeProprio, listaFacturador);
            Assert.DoesNotContain(nomeAlheio, listaFacturador);

            var listaGestor = await gestor.GetStringAsync("/Clientes?Q=" + Uri.EscapeDataString("Cliente d"));
            Assert.Contains(nomeProprio, listaGestor);
            Assert.Contains(nomeAlheio, listaGestor);

            Assert.Equal(HttpStatusCode.OK, (await facturador.GetAsync($"/Clientes/Detalhe/{idCliente}")).StatusCode);
        }
        finally
        {
            await app.ComScopeAsync(sp => DefinirSoPropriosAsync(sp, false));
        }
    }

    [Fact]
    public async Task Importacao_de_clientes_analisa_sem_gravar_e_respeita_o_modo_de_duplicados()
    {
        await app.ComScopeAsync(async sp =>
        {
            var importacao = sp.GetRequiredService<ServicoImportacao>();
            var db = sp.GetRequiredService<AppDbContext>();
            var existente = NovoNuit();
            var novo1 = NovoNuit();
            var novo2 = NovoNuit();
            await sp.GetRequiredService<ServicoClientes>().GuardarAsync(null, Cliente("Já Existia", existente));

            var csv = Encoding.Latin1.GetBytes(
                "Nome;NUIT;Cidade;Limite crédito;Desconto;Coluna estranha\r\n" +
                $"Novo Um;{novo1};Beira;10.000,00;5;x\r\n" +
                $"Novo Dois;{novo2};Nampula;;;\r\n" +
                $"Repetido no ficheiro;{novo1};Tete;;;\r\n" +
                $"Já Existia Actualizado;{existente};Matola;;;\r\n" +
                ";123;sem nome;;;\r\n" +
                $"NUIT curto;12345;Xai-Xai;;;\r\n");

            var analise = await importacao.AnalisarClientesAsync(csv, ModoDuplicados.Ignorar);
            Assert.Equal((2, 0, 1, 3), (analise.Novos, analise.Actualizados, analise.Ignorados, analise.Erros));
            Assert.Contains("colunaestranha", analise.ColunasIgnoradas);
            Assert.False(await db.Clientes.AnyAsync(c => c.Nuit == novo1));

            var feito = await importacao.ImportarClientesAsync(csv, ModoDuplicados.Actualizar);
            Assert.Equal((2, 1), (feito.Novos, feito.Actualizados));

            var importado = await db.Clientes.SingleAsync(c => c.Nuit == novo1);
            Assert.Equal(("Novo Um", "Beira", 10_000m, 5m), (importado.Nome, importado.Cidade, importado.LimiteCredito, importado.DescontoPadraoPct));
            var actualizado = await db.Clientes.SingleAsync(c => c.Nuit == existente);
            Assert.Equal(("Já Existia Actualizado", "Matola"), (actualizado.Nome, actualizado.Cidade));
            Assert.True(await db.Auditoria.AnyAsync(a => a.Accao == "Cliente.Importar"));
        });
    }

    [Fact]
    public async Task Importacao_de_artigos_aceita_taxa_por_codigo_ou_percentagem()
    {
        await app.ComScopeAsync(async sp =>
        {
            var importacao = sp.GetRequiredService<ServicoImportacao>();
            var db = sp.GetRequiredService<AppDbContext>();
            var p = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

            var csv = Encoding.UTF8.GetBytes(
                "Código;Descrição;Tipo;Unidade;Preço;IVA\n" +
                $"{p}-A;Consultoria;Serviço;h;2.500,00;NOR\n" +
                $"{p}-B;Cabo;Produto;cx;8900.50;16%\n" +
                $"{p}-C;Livro;Produto;un;300;0\n" +
                $"{p}-D;Taxa errada;Serviço;un;10;99\n");

            var r = await importacao.ImportarArtigosAsync(csv, ModoDuplicados.Ignorar);

            Assert.Equal((3, 1), (r.Novos, r.Erros));
            var b = await db.Artigos.SingleAsync(a => a.Codigo == $"{p}-B");
            Assert.Equal((TipoArtigo.Produto, 8_900.50m, "NOR"), (b.Tipo, b.Preco, b.CodigoTaxaIva));
            Assert.Equal("ISE", (await db.Artigos.SingleAsync(a => a.Codigo == $"{p}-C")).CodigoTaxaIva);
        });
    }

    [Fact]
    public async Task Codigo_de_artigo_e_unico_e_taxa_tem_de_existir()
    {
        await app.ComScopeAsync(async sp =>
        {
            var artigos = sp.GetRequiredService<ServicoArtigos>();
            var codigo = "T-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

            var (r1, _) = await artigos.GuardarAsync(null, new DadosArtigo(codigo.ToLowerInvariant(), "Teste", TipoArtigo.Servico, "un", 100, "NOR"));
            var (r2, _) = await artigos.GuardarAsync(null, new DadosArtigo(codigo, "Outro", TipoArtigo.Servico, "un", 100, "NOR"));
            var (r3, _) = await artigos.GuardarAsync(null, new DadosArtigo(codigo + "X", "Taxa inventada", TipoArtigo.Servico, "un", 100, "XYZ"));

            Assert.True(r1.Sucesso);
            Assert.Contains(r2.Erros, e => e.Contains("Já existe um artigo"));
            Assert.Contains(r3.Erros, e => e.Contains("taxa de IVA"));
        });
    }

    [Fact]
    public async Task Importacao_pela_pagina_em_dois_passos()
    {
        var cliente = await app.ClienteComSessaoAsync(PerfisPadrao.Administrador);
        var nuit = NovoNuit();
        var csv = Encoding.UTF8.GetBytes($"Nome;NUIT\nImportado Pela Página;{nuit}\n");

        var token = await AplicacaoFactory.TokenAntiforgeryAsync(cliente, "/Clientes/Importar");
        using var envio = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("Ignorar"), "Modo" },
            { new ByteArrayContent(csv), "Ficheiro", "clientes.csv" },
        };
        var analise = await cliente.PostAsync("/Clientes/Importar?handler=Analisar", envio);
        var html = await analise.Content.ReadAsStringAsync();
        Assert.Contains("Importar 1 clientes", html);

        var tokenFicheiro = System.Text.RegularExpressions.Regex.Match(html, "name=\"Token\" value=\"([^\"]+)\"").Groups[1].Value;
        var antiforgery = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var confirmar = await cliente.PostAsync("/Clientes/Importar?handler=Importar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Token"] = tokenFicheiro,
            ["Modo"] = "Ignorar",
            ["__RequestVerificationToken"] = antiforgery,
        }));

        Assert.Equal(HttpStatusCode.Redirect, confirmar.StatusCode);
        await app.ComScopeAsync(async sp =>
            Assert.True(await sp.GetRequiredService<AppDbContext>().Clientes.AnyAsync(c => c.Nuit == nuit)));
    }

    [Theory]
    [InlineData("../../appsettings.json")]
    [InlineData("importacoes/2026/naoexiste.csv")]
    public async Task Importar_com_token_adulterado_nao_le_ficheiros(string token)
    {
        var cliente = await app.ClienteComSessaoAsync(PerfisPadrao.Administrador);
        var antiforgery = await AplicacaoFactory.TokenAntiforgeryAsync(cliente, "/Clientes/Importar");

        var r = await cliente.PostAsync("/Clientes/Importar?handler=Importar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Token"] = token,
            ["Modo"] = "Ignorar",
            ["__RequestVerificationToken"] = antiforgery,
        }));

        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.EndsWith("/Clientes/Importar", r.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Pesquisa_global_respeita_as_permissoes()
    {
        var marca = "Global" + Guid.NewGuid().ToString("N")[..6];
        await app.ComScopeAsync(async sp =>
        {
            await sp.GetRequiredService<ServicoClientes>().GuardarAsync(null, Cliente($"Cliente {marca}"));
            await sp.GetRequiredService<ServicoArtigos>().GuardarAsync(null, new DadosArtigo(marca.ToUpperInvariant(), $"Artigo {marca}", TipoArtigo.Servico, "un", 1, "NOR"));
        });

        var admin = await app.ClienteComSessaoAsync(PerfisPadrao.Administrador);
        var html = await admin.GetStringAsync($"/Pesquisa?q={marca}");

        Assert.Contains($"Cliente {marca}", html);
        Assert.Contains($"Artigo {marca}", html);
    }

    private static async Task DefinirSoPropriosAsync(IServiceProvider sp, bool activo)
    {
        var empresas = sp.GetRequiredService<ServicoEmpresa>();
        var e = await empresas.ObterAsync();
        var r = await empresas.ActualizarDadosAsync(new DadosEmpresa(e.Nome, e.Nuit == InicializadorBaseDados.NuitPorPreencher ? "400000001" : e.Nuit,
            e.Slogan, e.Endereco, e.Cidade, e.Provincia, e.Telefones, e.Email, e.Site, e.CorPrimaria, e.RodapeTexto,
            e.PrazoPagamentoDias, e.LimiteNuitObrigatorio, e.ModoArredondamentoIva, activo));
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
    }
}
