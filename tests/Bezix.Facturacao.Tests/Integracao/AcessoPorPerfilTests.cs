using System.Net;
using Bezix.Facturacao.Domain.Seguranca;

namespace Bezix.Facturacao.Tests.Integracao;

/// <summary>Critério de aceitação: cada perfil só acede aos ecrãs permitidos (teste automático por perfil).</summary>
[Collection(ColeccaoAplicacao.Nome)]
public class AcessoPorPerfilTests(AplicacaoFactory app)
{
    public static TheoryData<string, string, bool> Matriz()
    {
        var d = new TheoryData<string, string, bool>();
        void Linha(string url, params string[] permitidos)
        {
            foreach (var p in PerfisPadrao.Todos.Select(x => x.Nome))
                d.Add(p, url, permitidos.Contains(p));
        }

        Linha("/", PerfisPadrao.Todos.Select(p => p.Nome).ToArray());
        Linha("/Configuracoes/Empresa", PerfisPadrao.Administrador);
        Linha("/Configuracoes/ContasBancarias", PerfisPadrao.Administrador);
        Linha("/Configuracoes/TaxasIva", PerfisPadrao.Administrador, PerfisPadrao.Contabilista);
        Linha("/Configuracoes/Series", PerfisPadrao.Administrador, PerfisPadrao.Contabilista);
        Linha("/Administracao/Utilizadores", PerfisPadrao.Administrador);
        Linha("/Administracao/Perfis", PerfisPadrao.Administrador);
        Linha("/Administracao/Auditoria", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Contabilista);
        Linha("/Conta/Seguranca", PerfisPadrao.Todos.Select(p => p.Nome).ToArray());

        // Fase 2: clientes e artigos ("Criar/editar clientes e artigos": Tesouraria só clientes; Contabilista e Consulta só vêem)
        Linha("/Clientes", PerfisPadrao.Todos.Select(p => p.Nome).ToArray());
        Linha("/Clientes/Editar", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador, PerfisPadrao.Tesouraria);
        Linha("/Clientes/Importar", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador, PerfisPadrao.Tesouraria);
        Linha("/Artigos", PerfisPadrao.Todos.Select(p => p.Nome).ToArray());
        Linha("/Artigos/Editar", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador);
        Linha("/Artigos/Importar", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador);

        // Fase 3: documentos ("Emitir FT, FR, FG": Tesouraria só FR; Contabilista e Consulta só consultam)
        Linha("/Documentos", PerfisPadrao.Todos.Select(p => p.Nome).ToArray());
        Linha("/Documentos/Editar?tipo=FT", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador);
        Linha("/Documentos/Editar?tipo=FR", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador, PerfisPadrao.Tesouraria);
        Linha("/Documentos/Editar?tipo=CT", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador);
        Linha("/Documentos/Aprovacoes", PerfisPadrao.Administrador, PerfisPadrao.Gestor);

        // Fase 4: "Registar recibos e alocar": Administrador, Gestor e Tesouraria
        Linha("/Recibos", PerfisPadrao.Todos.Select(p => p.Nome).ToArray());
        Linha("/Recibos/Novo", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Tesouraria);
        Linha("/ContaCorrente", PerfisPadrao.Todos.Select(p => p.Nome).ToArray());
        Linha("/ContaCorrente/Antiguidade", PerfisPadrao.Todos.Select(p => p.Nome).ToArray());

        // Fase 5: comunicação, automação e integrações
        Linha("/Comunicacao/Emails", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador, PerfisPadrao.Tesouraria);
        Linha("/Comunicacao/Modelos", PerfisPadrao.Administrador, PerfisPadrao.Gestor);
        Linha("/Comunicacao/Modelos/Editar/documento.FT", PerfisPadrao.Administrador, PerfisPadrao.Gestor);
        Linha("/Recibos/Comunicados", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Tesouraria);
        Linha("/ContaCorrente/Cobrancas", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador, PerfisPadrao.Tesouraria);
        Linha("/Automacao", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador, PerfisPadrao.Tesouraria);
        Linha("/Administracao/Integracoes", PerfisPadrao.Administrador);
        Linha("/Administracao/Integracoes/Entregas", PerfisPadrao.Administrador);

        // Fase 6: relatórios ("vendas": todos menos Tesouraria; "fiscais e exportação AT": Administrador, Gestor, Contabilista) e backups
        Linha("/Relatorios", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador, PerfisPadrao.Contabilista, PerfisPadrao.Consulta);
        Linha("/Relatorios/Vendas", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Facturador, PerfisPadrao.Contabilista, PerfisPadrao.Consulta);
        Linha("/Relatorios/Iva", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Contabilista);
        Linha("/Relatorios/ExportacaoAT", PerfisPadrao.Administrador, PerfisPadrao.Gestor, PerfisPadrao.Contabilista);
        Linha("/Administracao/Backups", PerfisPadrao.Administrador);
        return d;
    }

    [Theory]
    [MemberData(nameof(Matriz))]
    public async Task Perfil_so_acede_ao_que_a_matriz_permite(string perfil, string url, bool permitido)
    {
        var cliente = await app.ClienteComSessaoAsync(perfil);

        var r = await cliente.GetAsync(url);

        if (permitido)
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.StartsWith("/Conta/AcessoNegado", r.Headers.Location?.PathAndQuery);
        }
    }

    [Fact]
    public async Task Contabilista_ve_as_taxas_mas_nao_as_pode_alterar()
    {
        var cliente = await app.ClienteComSessaoAsync(PerfisPadrao.Contabilista);
        var html = await cliente.GetStringAsync("/Configuracoes/TaxasIva");
        Assert.DoesNotContain("Criar taxa", html);

        var token = await AplicacaoFactory.TokenAntiforgeryAsync(cliente, "/Configuracoes/TaxasIva");
        var r = await cliente.PostAsync("/Configuracoes/TaxasIva?handler=Criar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Nova.Codigo"] = "XPTO",
            ["Nova.Descricao"] = "Não devia ser criada",
            ["Nova.Percentagem"] = "5",
            ["Nova.VigenteDesde"] = "2026-01-01",
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/Conta/AcessoNegado", r.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task Sem_sessao_redirecciona_para_entrar()
    {
        var r = await app.CriarCliente().GetAsync("/Configuracoes/Empresa");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.StartsWith("/Conta/Entrar", r.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task Password_errada_e_recusada()
    {
        var r = await AplicacaoFactory.EntrarAsync(app.CriarCliente(), "admin@teste.local", "PasswordErrada1");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Email ou password errados", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Todas_as_permissoes_dos_perfis_padrao_existem_no_catalogo()
    {
        foreach (var perfil in PerfisPadrao.Todos)
            Assert.All(perfil.Permissoes, p => Assert.True(Permissoes.Existe(p), $"{perfil.Nome}: {p}"));
    }

    [Fact]
    public void Separacao_de_funcoes_quem_emite_nao_anula_e_tesouraria_nao_altera_facturas()
    {
        var facturador = PerfisPadrao.Todos.Single(p => p.Nome == PerfisPadrao.Facturador).Permissoes;
        Assert.DoesNotContain(Permissoes.Documentos.Anular, facturador);
        Assert.DoesNotContain(Permissoes.Documentos.EmitirNotaCredito, facturador);
        Assert.Contains(Permissoes.Documentos.PedirNotaCredito, facturador);

        var tesouraria = PerfisPadrao.Todos.Single(p => p.Nome == PerfisPadrao.Tesouraria).Permissoes;
        Assert.DoesNotContain(Permissoes.Documentos.EmitirFactura, tesouraria);
        Assert.DoesNotContain(Permissoes.Documentos.Anular, tesouraria);
    }
}
