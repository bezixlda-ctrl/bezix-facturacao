using System.IO.Compression;
using System.Text;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Application.Fiscal;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Application.Relatorios;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Backups;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SqlServer.Dac;

namespace Bezix.Facturacao.Tests.Integracao;

/// <summary>Fase 6: exportação AT, integridade, relatórios e backups (com restauro).</summary>
[Collection(ColeccaoAplicacao.Nome)]
public class FiscalTests(AplicacaoFactory app)
{
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2));

    private async Task<T> ComoAsync<T>(string perfil, Func<IServiceProvider, Task<T>> accao)
    {
        using var _ = UtilizadorTeste.Como(perfil);
        using var scope = app.Services.CreateScope();
        return await accao(scope.ServiceProvider);
    }

    private Task ComoAsync(string perfil, Func<IServiceProvider, Task> accao) =>
        ComoAsync<object?>(perfil, async sp => { await accao(sp); return null; });

    private sealed record Cenario(int Cliente, int Ft, int Nc, int Rc, int Ct);

    private Task<Cenario> PrepararAsync() => ComoAsync(PerfisPadrao.Administrador, async sp =>
    {
        var empresas = sp.GetRequiredService<ServicoEmpresa>();
        var e = await empresas.ObterAsync();
        if (e.Nuit == InicializadorBaseDados.NuitPorPreencher)
            await empresas.ActualizarDadosAsync(new DadosEmpresa(e.Nome, "400111222", e.Slogan, "Av. Teste, 1", e.Cidade, e.Provincia, e.Telefones,
                e.Email, e.Site, e.CorPrimaria, e.RodapeTexto, e.PrazoPagamentoDias, e.LimiteNuitObrigatorio, e.ModoArredondamentoIva));
        var cliente = await sp.GetRequiredService<ServicoClientes>().GuardarAsync(null, new DadosCliente(
            $"Cliente Fiscal {Guid.NewGuid():N}"[..28], TipoCliente.Empresa, Random.Shared.Next(400_000_000, 499_999_999).ToString(),
            "Rua Fiscal, 6", "Maputo", null, null, null, null, null, null, 0, null, null, null, null, true, null), aceitarNuitDuplicado: true);
        var db = sp.GetRequiredService<AppDbContext>();
        async Task<int> Serie(TipoDocumento tipo)
        {
            var codigo = "F" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var r = await sp.GetRequiredService<ServicoSeries>().CriarAsync(new NovaSerie(tipo, codigo, Hoje.Year, new DateOnly(Hoje.Year, 1, 1)));
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));
            return (await db.Series.SingleAsync(s => s.Codigo == codigo && s.TipoDoc == tipo)).Id;
        }
        return new Cenario(cliente.Id!.Value, await Serie(TipoDocumento.FT), await Serie(TipoDocumento.NC), await Serie(TipoDocumento.RC), await Serie(TipoDocumento.CT));
    });

    private Task<int> EmitirAsync(string perfil, TipoDocumento tipo, int serie, int cliente, decimal semIva, string taxa = "NOR",
        int? origem = null, string? motivo = null) =>
        ComoAsync(perfil, async sp =>
        {
            var (r, id) = await sp.GetRequiredService<ServicoDocumentos>().GuardarRascunhoAsync(null, new DadosRascunho(
                tipo, serie, cliente, Hoje, tipo is TipoDocumento.FT or TipoDocumento.CT ? Hoje.AddDays(30) : null, "MZN", 1, 0,
                null, null, null, null, null, null, origem, motivo,
                [new LinhaRascunho(null, "SRV", "Serviço de teste", "un", 1, semIva, 0, taxa)]));
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));
            var e = await sp.GetRequiredService<ServicoEmissao>().EmitirAsync(id!.Value);
            Assert.True(e.Tipo == ResultadoEmissaoTipo.Emitido, string.Join("; ", e.Resultado.Erros));
            return id.Value;
        });

    private static string Ler(ZipArchive z, string nome)
    {
        using var s = z.GetEntry(nome)!.Open();
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    // ------------------------------------------------------------------ Exportação AT

    [Fact]
    public async Task Exportacao_mensal_inclui_so_documentos_fiscais_marca_anulados_e_guarda_historico()
    {
        var c = await PrepararAsync();
        var ft = await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.FT, c.Ft, c.Cliente, 1_000);           // 1.160
        var ftAnulada = await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.FT, c.Ft, c.Cliente, 500);
        var nc = await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.NC, c.Nc, c.Cliente, 100, origem: ft, motivo: "Devolução");
        var cotacao = await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.CT, c.Ct, c.Cliente, 9_999);
        await ComoAsync(PerfisPadrao.Gestor, async sp =>
            Assert.True((await sp.GetRequiredService<ServicoEmissao>().AnularAsync(ftAnulada, "Emitida por engano")).Sucesso));

        // Facturador não exporta.
        var (recusado, _) = await ComoAsync(PerfisPadrao.Facturador, sp => sp.GetRequiredService<ServicoExportacaoAT>().GerarAsync(Hoje.Year, Hoje.Month));
        Assert.False(recusado.Sucesso);

        var (r, id) = await ComoAsync(PerfisPadrao.Contabilista, sp => sp.GetRequiredService<ServicoExportacaoAT>().GerarAsync(Hoje.Year, Hoje.Month));
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));

        await ComoAsync<object?>(PerfisPadrao.Contabilista, async sp =>
        {
            var servico = sp.GetRequiredService<ServicoExportacaoAT>();
            var db = sp.GetRequiredService<AppDbContext>();
            var e = await db.ExportacoesAT.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal(("csv-provisorio-v1", "contabilista@teste.local"), (e.Formato, e.GeradoPor));
            Assert.Matches($@"^AT_\d{{9}}_{Hoje:yyyyMM}\.zip$", e.NomeFicheiro);

            var numeros = await db.Documentos.AsNoTracking().Where(d => new[] { ft, ftAnulada, nc, cotacao }.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.NumeroFormatado!);
            var (stream, nome) = (await servico.FicheiroAsync(id!.Value))!.Value;
            using var mem = new MemoryStream();
            await stream.CopyToAsync(mem);
            await stream.DisposeAsync();
            Assert.Equal(e.HashSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(mem.ToArray())));
            using var zip = new ZipArchive(new MemoryStream(mem.ToArray()));
            Assert.Equal(["LEIA-ME.txt", "documentos.csv", "iva.csv", "linhas.csv"], zip.Entries.Select(x => x.FullName).Order(StringComparer.Ordinal));

            var documentos = Ler(zip, "documentos.csv").Split("\r\n");
            Assert.StartsWith("tipo,numero,estado,data,", documentos[0]);
            Assert.Contains(documentos, l => l.StartsWith($"FT,{numeros[ft]},N,") && l.Contains(",1000.00,160.00,1160.00,"));
            Assert.Contains(documentos, l => l.StartsWith($"FT,{numeros[ftAnulada]},A,") && l.Contains("Emitida por engano"));
            Assert.Contains(documentos, l => l.StartsWith($"NC,{numeros[nc]},N,") && l.Contains($",{numeros[ft]},Devolução,"));
            Assert.DoesNotContain(documentos, l => l.Contains(numeros[cotacao]));
            Assert.Equal($"{numeros[ft]},1,SRV,Serviço de teste,1,un,1000.00,0.00,1000.00,NOR,16.00,160.00,,1000.00",
                Ler(zip, "linhas.csv").Split("\r\n").Single(l => l.StartsWith(numeros[ft] + ",")));
            Assert.Contains($"{numeros[nc]},16.00,100.00,16.00,", Ler(zip, "iva.csv"));
            var leiaMe = Ler(zip, "LEIA-ME.txt");
            Assert.Contains("FORMATO PROVISÓRIO", leiaMe);
            Assert.Contains("Software:", leiaMe);
            Assert.DoesNotContain("pendente", leiaMe);

            // A anulada conta nos documentos mas não nos totais; a NC subtrai.
            Assert.True(e.NumDocs >= 3 && e.NumAnulados >= 1);
            Assert.Contains(await servico.ListarAsync(), x => x.Id == id);

            Assert.True((await servico.MarcarSubmetidaAsync(id.Value, "ED-2026-0001")).Sucesso);
            Assert.False((await servico.MarcarSubmetidaAsync(id.Value, "outra")).Sucesso);
            return null;
        });
    }

    [Fact]
    public async Task Exportacao_e_recusada_se_um_documento_foi_alterado_por_fora_da_aplicacao()
    {
        var c = await PrepararAsync();
        var doc = await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.FT, c.Ft, c.Cliente, 200);
        await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.FT, c.Ft, c.Cliente, 300);

        // Simula alguém a mexer directamente na base de dados, desligando o trigger de imutabilidade.
        async Task AlterarTotalAsync(string valor)
        {
            await using var cn = new SqlConnection(app.Ligacao);
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = $"""
                DISABLE TRIGGER {AppDbContext.TriggerDocumentos} ON Documentos;
                UPDATE Documentos SET Total = {valor} WHERE Id = {doc};
                ENABLE TRIGGER {AppDbContext.TriggerDocumentos} ON Documentos;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await AlterarTotalAsync("1.00");
        try
        {
            await ComoAsync<object?>(PerfisPadrao.Contabilista, async sp =>
            {
                var servico = sp.GetRequiredService<ServicoExportacaoAT>();
                var (r, _) = await servico.GerarAsync(Hoje.Year, Hoje.Month);
                Assert.False(r.Sucesso);
                Assert.Contains(r.Erros, e => e.Contains("cadeia de integridade") && e.Contains("n.º 1"));
                var estado = Assert.Single(await servico.VerificarIntegridadeAsync([c.Ft]));
                Assert.Equal(1, estado.PrimeiroComProblema);
                Assert.True(await sp.GetRequiredService<AppDbContext>().Auditoria.AnyAsync(a => a.Accao == "Integridade.Falha"));
                return null;
            });
        }
        finally
        {
            await AlterarTotalAsync("232.00");
        }
        var depois = await ComoAsync(PerfisPadrao.Contabilista, sp => sp.GetRequiredService<ServicoExportacaoAT>().VerificarIntegridadeAsync([c.Ft]));
        Assert.Null(Assert.Single(depois).PrimeiroComProblema);
    }

    // ------------------------------------------------------------------ Relatórios

    [Fact]
    public async Task Relatorios_de_vendas_e_iva_descontam_notas_de_credito_e_ignoram_anulados()
    {
        var c = await PrepararAsync();
        var ft = await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.FT, c.Ft, c.Cliente, 1_000);
        await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.FT, c.Ft, c.Cliente, 400, taxa: "ISE");
        await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.NC, c.Nc, c.Cliente, 250, origem: ft, motivo: "Desconto comercial");
        var anulada = await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.FT, c.Ft, c.Cliente, 7_777);
        await ComoAsync(PerfisPadrao.Gestor, async sp =>
            Assert.True((await sp.GetRequiredService<ServicoEmissao>().AnularAsync(anulada, "Teste de relatório")).Sucesso));

        await ComoAsync<object?>(PerfisPadrao.Gestor, async sp =>
        {
            var rel = sp.GetRequiredService<ServicoRelatorios>();
            var cliente = (await rel.VendasAsync(Hoje, Hoje, AgruparVendas.Cliente)).Single(l => l.Descricao.StartsWith("Cliente Fiscal") &&
                l.Chave == sp.GetRequiredService<AppDbContext>().Clientes.Single(x => x.Id == c.Cliente).Nuit);
            Assert.Equal((3, 1_150m, 120m, 1_270m), (cliente.Documentos, cliente.Base, cliente.Iva, cliente.Total));

            var anulados = await rel.AnuladosAsync(Hoje, Hoje);
            Assert.Contains(anulados, a => a.Id == anulada && a.Motivo == "Teste de relatório");
            return null;
        });

        // Mapa de IVA: só para quem tem relatórios fiscais.
        Assert.Empty(await ComoAsync(PerfisPadrao.Facturador, sp => sp.GetRequiredService<ServicoRelatorios>().MapaIvaAsync(Hoje, Hoje)));
        var mapa = await ComoAsync(PerfisPadrao.Contabilista, sp => sp.GetRequiredService<ServicoRelatorios>().MapaIvaAsync(Hoje, Hoje));
        var normal = mapa.Single(l => l.TaxaPct == 16m);
        Assert.True(normal.BaseCreditada >= 250m && normal.IvaCreditado >= 40m);
        Assert.Equal(normal.BaseFacturada - normal.BaseCreditada, normal.Base);
        Assert.Contains(mapa, l => l.TaxaPct == 0m && l.MotivoIsencao is not null && l.BaseFacturada >= 400m);
    }

    [Fact]
    public async Task Facturador_so_ve_as_suas_vendas()
    {
        var c = await PrepararAsync();
        await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.FT, c.Ft, c.Cliente, 5_000);
        await EmitirAsync(PerfisPadrao.Facturador, TipoDocumento.FT, c.Ft, c.Cliente, 100);

        var porTipo = await ComoAsync(PerfisPadrao.Facturador, sp => sp.GetRequiredService<ServicoRelatorios>().VendasAsync(Hoje, Hoje, AgruparVendas.Tipo));
        var gestorTotal = await ComoAsync(PerfisPadrao.Gestor, async sp =>
            (await sp.GetRequiredService<ServicoRelatorios>().VendasAsync(Hoje, Hoje, AgruparVendas.Tipo)).Sum(l => l.Base));
        var facturadorTotal = porTipo.Sum(l => l.Base);
        Assert.True(facturadorTotal >= 100m);
        Assert.True(gestorTotal - facturadorTotal >= 5_000m);
    }

    // ------------------------------------------------------------------ Backups

    [Fact]
    public async Task Backup_cifrado_e_restaurado_numa_base_nova_com_os_mesmos_documentos_e_hashes()
    {
        var c = await PrepararAsync();
        var doc = await EmitirAsync(PerfisPadrao.Gestor, TipoDocumento.FT, c.Ft, c.Cliente, 4_321);

        var resultado = await ComoAsync(PerfisPadrao.Administrador, sp => sp.GetRequiredService<ServicoBackups>().ExecutarAsync(forcar: true));
        Assert.True(resultado.Executado, resultado.Mensagem);
        Assert.Equal(2, resultado.Enviados.Count);

        // Sem "forcar", não repete dentro do intervalo.
        var repetido = await ComoAsync(PerfisPadrao.Administrador, sp => sp.GetRequiredService<ServicoBackups>().ExecutarAsync());
        Assert.False(repetido.Executado);

        var bd = resultado.Enviados.Single(n => n.Contains("-bd.bacpac"));
        var verificacao = await ComoAsync(PerfisPadrao.Administrador, sp => sp.GetRequiredService<ServicoBackups>().VerificarAsync(bd));
        Assert.True(verificacao.Valido, verificacao.Detalhe);

        // O ficheiro no destino não tem texto legível (está cifrado).
        var cifrado = await File.ReadAllBytesAsync(Path.Combine(app.PastaBackups, bd));
        Assert.Equal("BZXBKP01", Encoding.ASCII.GetString(cifrado, 0, 8));
        Assert.DoesNotContain("model.xml", Encoding.ASCII.GetString(cifrado));

        // Restauro: decifrar e importar o BACPAC numa base nova.
        var temp = Directory.CreateTempSubdirectory("bezix-restauro-");
        var baseRestaurada = $"BezixRestauro_{Guid.NewGuid():N}";
        var ligacaoRestaurada = new SqlConnectionStringBuilder(app.Ligacao) { InitialCatalog = baseRestaurada }.ConnectionString;
        try
        {
            var bacpac = Path.Combine(temp.FullName, "bd.bacpac");
            await ServicoBackups.DecifrarFicheiroAsync(Path.Combine(app.PastaBackups, bd), bacpac, Convert.FromBase64String(app.ChaveBackups));
            using (var pacote = BacPackage.Load(bacpac))
                new DacServices(ligacaoRestaurada).ImportBacpac(pacote, baseRestaurada);

            var original = await ComoAsync(PerfisPadrao.Administrador, sp => sp.GetRequiredService<AppDbContext>().Documentos.AsNoTracking()
                .Where(d => d.SerieId == c.Ft).OrderBy(d => d.Numero).ToListAsync());
            var opcoes = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ligacaoRestaurada).Options;
            await using var restaurada = new AppDbContext(opcoes);
            var copia = await restaurada.Documentos.AsNoTracking().Where(d => d.SerieId == c.Ft).OrderBy(d => d.Numero).ToListAsync();
            Assert.Equal(original.Select(d => (d.Id, d.NumeroFormatado, d.Total, d.Hash)), copia.Select(d => (d.Id, d.NumeroFormatado, d.Total, d.Hash)));
            Assert.Contains(copia, d => d.Id == doc && d.Total == 5_012.36m);
            Assert.Null(HashDocumento.VerificarCadeia(copia));

            // Os triggers de imutabilidade vêm no backup.
            await using var cn = new SqlConnection(ligacaoRestaurada);
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = $"UPDATE Documentos SET Total = 1 WHERE Id = {doc}";
            await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteNonQueryAsync());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using (var cn = new SqlConnection(new SqlConnectionStringBuilder(app.Ligacao) { InitialCatalog = "master" }.ConnectionString))
            {
                await cn.OpenAsync();
                await using var cmd = cn.CreateCommand();
                cmd.CommandText = $"IF DB_ID('{baseRestaurada}') IS NOT NULL BEGIN ALTER DATABASE [{baseRestaurada}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{baseRestaurada}]; END";
                await cmd.ExecuteNonQueryAsync();
            }
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Backup_alterado_no_destino_e_detectado_na_verificacao()
    {
        var resultado = await ComoAsync(PerfisPadrao.Administrador, sp => sp.GetRequiredService<ServicoBackups>().ExecutarAsync(forcar: true));
        Assert.True(resultado.Executado, resultado.Mensagem);
        var ficheiros = resultado.Enviados.Single(n => n.Contains("-ficheiros.zip"));
        var caminho = Path.Combine(app.PastaBackups, ficheiros);
        var bytes = await File.ReadAllBytesAsync(caminho);
        bytes[bytes.Length / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(caminho, bytes);

        var v = await ComoAsync(PerfisPadrao.Administrador, sp => sp.GetRequiredService<ServicoBackups>().VerificarAsync(ficheiros));
        Assert.False(v.Valido);
    }
}
