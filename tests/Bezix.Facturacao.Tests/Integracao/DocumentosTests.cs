using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bezix.Facturacao.Tests.Integracao;

[Collection(ColeccaoAplicacao.Nome)]
public class DocumentosTests(AplicacaoFactory app)
{
    private static readonly Random Aleatorio = new();

    // ------------------------------------------------------------------ Auxiliares

    private async Task<T> ComoAsync<T>(string perfil, Func<IServiceProvider, Task<T>> accao)
    {
        using var _ = UtilizadorTeste.Como(perfil);
        using var scope = app.Services.CreateScope();
        return await accao(scope.ServiceProvider);
    }

    private Task ComoAsync(string perfil, Func<IServiceProvider, Task> accao) =>
        ComoAsync<object?>(perfil, async sp => { await accao(sp); return null; });

    /// <summary>Empresa com NUIT, cliente novo e uma série FT nova (para os números serem previsíveis).</summary>
    private async Task<(int ClienteId, int SerieId)> PrepararAsync(TipoDocumento tipo = TipoDocumento.FT, TipoCliente tipoCliente = TipoCliente.Empresa, bool comNuit = true)
    {
        return await ComoAsync(PerfisPadrao.Administrador, async sp =>
        {
            var empresas = sp.GetRequiredService<ServicoEmpresa>();
            var e = await empresas.ObterAsync();
            if (e.Nuit == InicializadorBaseDados.NuitPorPreencher)
                await empresas.ActualizarDadosAsync(new DadosEmpresa(e.Nome, "400111222", e.Slogan, "Av. Teste, 1", e.Cidade, e.Provincia, e.Telefones,
                    e.Email, e.Site, e.CorPrimaria, e.RodapeTexto, e.PrazoPagamentoDias, e.LimiteNuitObrigatorio, e.ModoArredondamentoIva));

            var nuit = comNuit ? Aleatorio.Next(400_000_000, 499_999_999).ToString() : null;
            var cliente = await sp.GetRequiredService<ServicoClientes>().GuardarAsync(null, new DadosCliente(
                $"Cliente Documentos {Guid.NewGuid():N}"[..30], tipoCliente, nuit, "Rua 1", "Maputo", null, null, "cliente@teste.local", "Sr. Teste",
                null, null, 0, null, null, null, null, true, null), aceitarNuitDuplicado: true);

            var codigo = "T" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var ano = DateTime.UtcNow.AddHours(2).Year;
            var r = await sp.GetRequiredService<ServicoSeries>().CriarAsync(new NovaSerie(tipo, codigo, ano, new DateOnly(ano, 1, 1)));
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));
            var serie = await sp.GetRequiredService<AppDbContext>().Series.SingleAsync(s => s.Codigo == codigo && s.TipoDoc == tipo);
            return (cliente.Id!.Value, serie.Id);
        });
    }

    private static DadosRascunho Rascunho(TipoDocumento tipo, int serieId, int clienteId, params LinhaRascunho[] linhas) => new(
        tipo, serieId, clienteId, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2)), null, "MZN", 1, 0, null,
        tipo == TipoDocumento.FR ? MetodoPagamento.MPesa : null, null, null, null, null, null, null, linhas);

    private static LinhaRascunho Linha(decimal qtd, decimal preco, decimal desconto = 0, string taxa = "NOR") =>
        new(null, null, "Serviço de teste", "un", qtd, preco, desconto, taxa);

    private async Task<int> CriarRascunhoAsync(string perfil, DadosRascunho d) =>
        await ComoAsync(perfil, async sp =>
        {
            var (r, id) = await sp.GetRequiredService<ServicoDocumentos>().GuardarRascunhoAsync(null, d);
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));
            return id!.Value;
        });

    private Task<ResultadoEmissao> EmitirAsync(string perfil, int id) =>
        ComoAsync(perfil, sp => sp.GetRequiredService<ServicoEmissao>().EmitirAsync(id));

    private async Task<Documento> LerAsync(int id) =>
        await ComoAsync(PerfisPadrao.Administrador, sp =>
            sp.GetRequiredService<AppDbContext>().Documentos.AsNoTracking().Include(d => d.Linhas).Include(d => d.IvaResumo).SingleAsync(d => d.Id == id));

    // ------------------------------------------------------------------ Emissão

    [Fact]
    public async Task Emitir_factura_modelo_numera_calcula_encadeia_e_guarda_PDF()
    {
        var (cliente, serie) = await PrepararAsync();
        var id = await CriarRascunhoAsync(PerfisPadrao.Facturador, Rascunho(TipoDocumento.FT, serie, cliente, Linha(1, 30_804.10m)) with { DescontoGlobalPct = 20 });

        // O Facturador tem limite de 10%: com 20% de desconto vai para aprovação; o Gestor emite directamente.
        var r = await EmitirAsync(PerfisPadrao.Gestor, id);

        Assert.Equal(ResultadoEmissaoTipo.Emitido, r.Tipo);
        var doc = await LerAsync(id);
        Assert.Equal(EstadoDocumento.Emitido, doc.Estado);
        Assert.Equal(1, doc.Numero);
        Assert.Matches(@"^FT T[0-9A-F]{6}\d{4}/0001$", doc.NumeroFormatado);
        Assert.Equal((24_643.28m, 3_942.92m, 28_586.20m), (doc.BaseTributavel, doc.TotalIva, doc.Total));
        Assert.Equal(doc.Total, doc.TotalMzn);
        Assert.Null(doc.HashAnterior);
        Assert.Equal(HashDocumento.Calcular(doc), doc.Hash);
        Assert.NotNull(doc.PdfPath);
        Assert.NotNull(doc.EmitenteJson);
    }

    [Fact]
    public async Task Emissoes_simultaneas_na_mesma_serie_nao_duplicam_nem_saltam_numeros()
    {
        const int quantos = 12;
        var (cliente, serie) = await PrepararAsync();
        var ids = new List<int>();
        for (var i = 0; i < quantos; i++)
            ids.Add(await CriarRascunhoAsync(PerfisPadrao.Gestor, Rascunho(TipoDocumento.FT, serie, cliente, Linha(1, 100 + i))));

        // Cada emissão no seu scope (como pedidos de utilizadores diferentes), todas ao mesmo tempo.
        var resultados = await Task.WhenAll(ids.Select(id => Task.Run(() => EmitirAsync(PerfisPadrao.Gestor, id))));

        Assert.All(resultados, r => Assert.Equal(ResultadoEmissaoTipo.Emitido, r.Tipo));
        var docs = await ComoAsync(PerfisPadrao.Administrador, sp =>
            sp.GetRequiredService<AppDbContext>().Documentos.AsNoTracking().Where(d => d.SerieId == serie).OrderBy(d => d.Numero).ToListAsync());
        Assert.Equal(Enumerable.Range(1, quantos), docs.Select(d => d.Numero!.Value));
        Assert.Null(HashDocumento.VerificarCadeia(docs));

        var serieFinal = await ComoAsync(PerfisPadrao.Administrador, sp => sp.GetRequiredService<AppDbContext>().Series.AsNoTracking().SingleAsync(s => s.Id == serie));
        Assert.Equal(quantos + 1, serieFinal.ProximoNumero);
    }

    [Fact]
    public async Task Documento_emitido_nao_se_altera_pela_aplicacao_nem_por_SQL_e_a_tentativa_fica_na_auditoria()
    {
        var (cliente, serie) = await PrepararAsync();
        var id = await CriarRascunhoAsync(PerfisPadrao.Gestor, Rascunho(TipoDocumento.FT, serie, cliente, Linha(2, 500)));
        Assert.Equal(ResultadoEmissaoTipo.Emitido, (await EmitirAsync(PerfisPadrao.Gestor, id)).Tipo);

        await ComoAsync(PerfisPadrao.Administrador, async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();

            // Pela aplicação (EF): interceptor recusa e regista.
            var doc = await db.Documentos.Include(d => d.Linhas).SingleAsync(d => d.Id == id);
            doc.Total = 1;
            await Assert.ThrowsAsync<DocumentoImutavelException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            Assert.True(await db.Auditoria.AnyAsync(a => a.Accao == "Documento.TentativaAlteracao" && a.EntidadeId == id.ToString()));

            var linha = await db.DocumentoLinhas.FirstAsync(l => l.DocumentoId == id);
            db.DocumentoLinhas.Remove(linha);
            await Assert.ThrowsAsync<DocumentoImutavelException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            // Por SQL directo: os triggers recusam.
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"UPDATE Documentos SET Total = 1 WHERE Id = {id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"UPDATE DocumentoLinhas SET PrecoUnitario = 1 WHERE DocumentoId = {id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"DELETE FROM DocumentoLinhas WHERE DocumentoId = {id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"DELETE FROM Documentos WHERE Id = {id}"));

            // O caminho do PDF pode mudar (ex.: regenerar um PDF perdido).
            await db.Database.ExecuteSqlAsync($"UPDATE Documentos SET PdfPath = PdfPath WHERE Id = {id}");
        });

        var final = await LerAsync(id);
        Assert.Equal(1_160m, final.Total);
        Assert.Single(final.Linhas);
    }

    [Fact]
    public async Task Data_anterior_ao_ultimo_documento_da_serie_e_recusada()
    {
        var (cliente, serie) = await PrepararAsync();
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2));
        var primeiro = await CriarRascunhoAsync(PerfisPadrao.Gestor, Rascunho(TipoDocumento.FT, serie, cliente, Linha(1, 100)));
        Assert.Equal(ResultadoEmissaoTipo.Emitido, (await EmitirAsync(PerfisPadrao.Gestor, primeiro)).Tipo);

        var atrasado = await CriarRascunhoAsync(PerfisPadrao.Gestor, Rascunho(TipoDocumento.FT, serie, cliente, Linha(1, 100)) with { Data = hoje.AddDays(-1) });
        var r = await EmitirAsync(PerfisPadrao.Gestor, atrasado);

        if (hoje.DayOfYear == 1) return; // no dia 1 de Janeiro, ontem pertence a outro ano (também é recusado, por outra regra)
        Assert.Equal(ResultadoEmissaoTipo.Recusado, r.Tipo);
        Assert.Contains(r.Resultado.Erros, e => e.Contains("anterior à do último documento"));
    }

    [Fact]
    public async Task Empresa_sem_NUIT_nao_pode_receber_factura()
    {
        var (cliente, serie) = await PrepararAsync(comNuit: false);
        var id = await CriarRascunhoAsync(PerfisPadrao.Gestor, Rascunho(TipoDocumento.FT, serie, cliente, Linha(1, 100)));

        var r = await EmitirAsync(PerfisPadrao.Gestor, id);

        Assert.Equal(ResultadoEmissaoTipo.Recusado, r.Tipo);
        Assert.Contains(r.Resultado.Erros, e => e.Contains("NUIT do cliente"));
    }

    [Theory]
    [InlineData("USD", 63.85)]
    [InlineData("EUR", 1)]
    [InlineData("MZN", 2)]
    public async Task Documentos_so_em_meticais(string moeda, double cambio)
    {
        var (cliente, serie) = await PrepararAsync();
        var (r, _) = await ComoAsync(PerfisPadrao.Gestor, sp => sp.GetRequiredService<ServicoDocumentos>()
            .GuardarRascunhoAsync(null, Rascunho(TipoDocumento.FT, serie, cliente, Linha(1, 1)) with { Moeda = moeda, Cambio = (decimal)cambio }));

        Assert.Contains(r.Erros, e => e.Contains("meticais (MZN)"));
    }

    [Fact]
    public async Task Factura_em_meticais_tem_valores_fiscais_iguais_aos_do_documento()
    {
        var (cliente, serie) = await PrepararAsync();
        var id = await CriarRascunhoAsync(PerfisPadrao.Gestor, Rascunho(TipoDocumento.FT, serie, cliente, Linha(10, 150)));
        Assert.Equal(ResultadoEmissaoTipo.Emitido, (await EmitirAsync(PerfisPadrao.Gestor, id)).Tipo);

        var doc = await LerAsync(id);
        Assert.Equal(("MZN", 1m), (doc.Moeda, doc.Cambio));
        Assert.Equal((doc.BaseTributavel, doc.TotalIva, doc.Total), (doc.BaseTributavelMzn, doc.TotalIvaMzn, doc.TotalMzn));
    }

    // ------------------------------------------------------------------ Notas de crédito e anulação

    [Fact]
    public async Task Nota_de_credito_limitada_ao_valor_por_creditar()
    {
        var (cliente, serieFt) = await PrepararAsync();
        var serieNc = (await PrepararAsync(TipoDocumento.NC)).SerieId;
        var ft = await CriarRascunhoAsync(PerfisPadrao.Gestor, Rascunho(TipoDocumento.FT, serieFt, cliente, Linha(10, 100)));
        Assert.Equal(ResultadoEmissaoTipo.Emitido, (await EmitirAsync(PerfisPadrao.Gestor, ft)).Tipo); // total 1.160

        async Task<ResultadoEmissao> Nc(decimal qtd) =>
            await EmitirAsync(PerfisPadrao.Gestor, await CriarRascunhoAsync(PerfisPadrao.Gestor,
                Rascunho(TipoDocumento.NC, serieNc, cliente, Linha(qtd, 100)) with { DocOrigemId = ft, MotivoCorreccao = "Devolução parcial" }));

        Assert.Equal(ResultadoEmissaoTipo.Emitido, (await Nc(6)).Tipo);       // 696 creditados, faltam 464
        var excesso = await Nc(5);                                           // 580 > 464
        Assert.Equal(ResultadoEmissaoTipo.Recusado, excesso.Tipo);
        Assert.Contains(excesso.Resultado.Erros, e => e.Contains("passa o valor ainda por creditar"));
        Assert.Equal(ResultadoEmissaoTipo.Emitido, (await Nc(4)).Tipo);       // 464: fecha
        Assert.Equal(0m, await ComoAsync(PerfisPadrao.Gestor, sp => sp.GetRequiredService<ServicoDocumentos>().ValorPorCreditarAsync(ft)));
    }

    [Fact]
    public async Task Anulacao_exige_permissao_motivo_e_documento_sem_notas()
    {
        var (cliente, serieFt) = await PrepararAsync();
        var serieNc = (await PrepararAsync(TipoDocumento.NC)).SerieId;
        var ft = await CriarRascunhoAsync(PerfisPadrao.Gestor, Rascunho(TipoDocumento.FT, serieFt, cliente, Linha(1, 100)));
        await EmitirAsync(PerfisPadrao.Gestor, ft);

        var semPermissao = await ComoAsync(PerfisPadrao.Facturador, sp => sp.GetRequiredService<ServicoEmissao>().AnularAsync(ft, "Erro de preço"));
        Assert.Contains("não permite anular", semPermissao.Erros[0]);
        var semMotivo = await ComoAsync(PerfisPadrao.Gestor, sp => sp.GetRequiredService<ServicoEmissao>().AnularAsync(ft, ""));
        Assert.Contains("motivo", semMotivo.Erros[0]);

        var nc = await CriarRascunhoAsync(PerfisPadrao.Gestor,
            Rascunho(TipoDocumento.NC, serieNc, cliente, Linha(1, 10)) with { DocOrigemId = ft, MotivoCorreccao = "Desconto" });
        await EmitirAsync(PerfisPadrao.Gestor, nc);
        var comNota = await ComoAsync(PerfisPadrao.Gestor, sp => sp.GetRequiredService<ServicoEmissao>().AnularAsync(ft, "Erro de preço"));
        Assert.Contains("notas associadas", comNota.Erros[0]);

        Assert.True((await ComoAsync(PerfisPadrao.Gestor, sp => sp.GetRequiredService<ServicoEmissao>().AnularAsync(nc, "Emitida por engano"))).Sucesso);
        Assert.True((await ComoAsync(PerfisPadrao.Gestor, sp => sp.GetRequiredService<ServicoEmissao>().AnularAsync(ft, "Erro de preço"))).Sucesso);

        var anulada = await LerAsync(ft);
        Assert.Equal((EstadoDocumento.Anulado, "Erro de preço", 1), (anulada.Estado, anulada.MotivoAnulacao, anulada.Numero));
    }

    // ------------------------------------------------------------------ Aprovações e permissões

    [Fact]
    public async Task Desconto_acima_do_limite_do_facturador_aguarda_aprovacao_do_gestor()
    {
        var (cliente, serie) = await PrepararAsync();
        var id = await CriarRascunhoAsync(PerfisPadrao.Facturador, Rascunho(TipoDocumento.FT, serie, cliente, Linha(1, 1000, desconto: 15)));

        var pedido = await EmitirAsync(PerfisPadrao.Facturador, id);
        Assert.Equal(ResultadoEmissaoTipo.AguardaAprovacao, pedido.Tipo);
        Assert.Equal(EstadoDocumento.AguardaAprovacao, (await LerAsync(id)).Estado);

        var naoPode = await ComoAsync(PerfisPadrao.Facturador, sp => sp.GetRequiredService<ServicoEmissao>().AprovarAsync(id));
        Assert.Equal(ResultadoEmissaoTipo.Recusado, naoPode.Tipo);

        var aprovado = await ComoAsync(PerfisPadrao.Gestor, sp => sp.GetRequiredService<ServicoEmissao>().AprovarAsync(id));
        Assert.Equal(ResultadoEmissaoTipo.Emitido, aprovado.Tipo);
        var aprovacao = await ComoAsync(PerfisPadrao.Administrador, sp => sp.GetRequiredService<AppDbContext>().Aprovacoes.AsNoTracking().SingleAsync(a => a.DocumentoId == id));
        Assert.Equal((EstadoAprovacao.Aprovada, TipoAprovacao.Desconto), (aprovacao.Estado, aprovacao.Tipo));
    }

    [Fact]
    public async Task Desconto_dentro_do_limite_e_emitido_pelo_facturador()
    {
        var (cliente, serie) = await PrepararAsync();
        var id = await CriarRascunhoAsync(PerfisPadrao.Facturador, Rascunho(TipoDocumento.FT, serie, cliente, Linha(1, 1000, desconto: 10)));
        Assert.Equal(ResultadoEmissaoTipo.Emitido, (await EmitirAsync(PerfisPadrao.Facturador, id)).Tipo);
    }

    [Fact]
    public async Task Tesouraria_emite_factura_recibo_mas_nao_factura()
    {
        var (cliente, serieFr) = await PrepararAsync(TipoDocumento.FR);
        var serieFt = (await PrepararAsync()).SerieId;

        var fr = await CriarRascunhoAsync(PerfisPadrao.Tesouraria, Rascunho(TipoDocumento.FR, serieFr, cliente, Linha(1, 100)));
        Assert.Equal(ResultadoEmissaoTipo.Emitido, (await EmitirAsync(PerfisPadrao.Tesouraria, fr)).Tipo);

        var (r, _) = await ComoAsync(PerfisPadrao.Tesouraria, sp => sp.GetRequiredService<ServicoDocumentos>()
            .GuardarRascunhoAsync(null, Rascunho(TipoDocumento.FT, serieFt, cliente, Linha(1, 100))));
        Assert.False(r.Sucesso);
    }

    // ------------------------------------------------------------------ Conversão, PDF e email

    [Fact]
    public async Task Cotacao_convertida_em_factura_copia_linhas_e_mantem_ligacao()
    {
        var (cliente, serieCt) = await PrepararAsync(TipoDocumento.CT);
        await PrepararAsync(); // garante empresa com NUIT
        var ct = await CriarRascunhoAsync(PerfisPadrao.Facturador, Rascunho(TipoDocumento.CT, serieCt, cliente, Linha(3, 250), Linha(1, 99)));
        Assert.Equal(ResultadoEmissaoTipo.Emitido, (await EmitirAsync(PerfisPadrao.Facturador, ct)).Tipo);

        var (r, ftId) = await ComoAsync(PerfisPadrao.Facturador, sp => sp.GetRequiredService<ServicoDocumentos>().ConverterAsync([ct], TipoDocumento.FT));

        Assert.True(r.Sucesso, string.Join("; ", r.Erros));
        var ft = await ComoAsync(PerfisPadrao.Facturador, sp => sp.GetRequiredService<ServicoDocumentos>().ObterAsync(ftId!.Value));
        Assert.Equal((TipoDocumento.FT, EstadoDocumento.Rascunho, 2), (ft!.TipoDoc, ft.Estado, ft.Linhas.Count));
        Assert.Equal(ct, Assert.Single(ft.Origens).OrigemId);
    }

    [Fact]
    public async Task PDF_impressao_e_email_usam_o_mesmo_ficheiro_e_o_email_leva_anexo()
    {
        var (cliente, serie) = await PrepararAsync();
        var id = await CriarRascunhoAsync(PerfisPadrao.Gestor, Rascunho(TipoDocumento.FT, serie, cliente, Linha(1, 100)));
        await EmitirAsync(PerfisPadrao.Gestor, id);

        await ComoAsync(PerfisPadrao.Gestor, async sp =>
        {
            var pdfs = sp.GetRequiredService<ServicoPdfDocumentos>();
            var a = await pdfs.ObterAsync(id);
            var b = await pdfs.ObterAsync(id);
            Assert.NotNull(a);
            Assert.True(a.Conteudo.AsSpan().StartsWith("%PDF"u8));
            Assert.Equal(a.Conteudo, b!.Conteudo);                    // o guardado, sempre igual
            Assert.NotEqual(a.Conteudo, (await pdfs.ObterAsync(id, segundaVia: true))!.Conteudo); // 2.ª via tem marca

            var comunicacao = sp.GetRequiredService<ServicoComunicacao>();
            var email = await comunicacao.PrepararDocumentoAsync(id);
            Assert.Equal("cliente@teste.local", email!.Para);
            Assert.Contains("Factura", email.Assunto);

            var antes = Directory.Exists(app.PastaEmails) ? Directory.GetFiles(app.PastaEmails).Length : 0;
            var r = await comunicacao.EnviarDocumentoAsync(id, email);
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));

            var eml = Directory.GetFiles(app.PastaEmails).OrderByDescending(File.GetCreationTimeUtc).First();
            Assert.Equal(antes + 1, Directory.GetFiles(app.PastaEmails).Length);
            var mensagem = await MimeKit.MimeMessage.LoadAsync(eml);
            var anexo = Assert.Single(mensagem.Attachments.OfType<MimeKit.MimePart>());
            Assert.EndsWith(".pdf", anexo.FileName);
            using var conteudo = new MemoryStream();
            Assert.NotNull(anexo.Content);
            await anexo.Content.DecodeToAsync(conteudo);
            Assert.Equal(a.Conteudo, conteudo.ToArray());
        });
    }

    [Fact]
    public async Task Rascunho_pode_ser_apagado_mas_documento_emitido_nao()
    {
        var (cliente, serie) = await PrepararAsync();
        var rascunho = await CriarRascunhoAsync(PerfisPadrao.Gestor, Rascunho(TipoDocumento.FT, serie, cliente, Linha(1, 1)));
        var emitido = await CriarRascunhoAsync(PerfisPadrao.Gestor, Rascunho(TipoDocumento.FT, serie, cliente, Linha(1, 1)));
        await EmitirAsync(PerfisPadrao.Gestor, emitido);

        await ComoAsync(PerfisPadrao.Gestor, async sp =>
        {
            var docs = sp.GetRequiredService<ServicoDocumentos>();
            Assert.True((await docs.ApagarRascunhoAsync(rascunho)).Sucesso);
            Assert.False((await docs.ApagarRascunhoAsync(emitido)).Sucesso);
        });
    }
}
