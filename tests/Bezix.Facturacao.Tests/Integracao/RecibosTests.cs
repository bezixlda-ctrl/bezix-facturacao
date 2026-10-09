using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Recebimentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bezix.Facturacao.Tests.Integracao;

[Collection(ColeccaoAplicacao.Nome)]
public class RecibosTests(AplicacaoFactory app)
{
    private static readonly Random Aleatorio = new();
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2));

    private async Task<T> ComoAsync<T>(string perfil, Func<IServiceProvider, Task<T>> accao)
    {
        using var _ = UtilizadorTeste.Como(perfil);
        using var scope = app.Services.CreateScope();
        return await accao(scope.ServiceProvider);
    }

    /// <summary>Cliente novo e séries FT/NC/RC novas (só deste teste), com a empresa preparada para emitir.</summary>
    private async Task<(int Cliente, int SerieFt, int SerieNc, int SerieRc)> PrepararAsync()
    {
        return await ComoAsync(PerfisPadrao.Administrador, async sp =>
        {
            var empresas = sp.GetRequiredService<ServicoEmpresa>();
            var e = await empresas.ObterAsync();
            if (e.Nuit == InicializadorBaseDados.NuitPorPreencher)
                await empresas.ActualizarDadosAsync(new DadosEmpresa(e.Nome, "400111222", e.Slogan, "Av. Teste, 1", e.Cidade, e.Provincia, e.Telefones,
                    e.Email, e.Site, e.CorPrimaria, e.RodapeTexto, e.PrazoPagamentoDias, e.LimiteNuitObrigatorio, e.ModoArredondamentoIva));

            var cliente = await sp.GetRequiredService<ServicoClientes>().GuardarAsync(null, new DadosCliente(
                $"Cliente Recibos {Guid.NewGuid():N}"[..28], TipoCliente.Empresa, Aleatorio.Next(400_000_000, 499_999_999).ToString(),
                "Rua 2", "Beira", null, null, "pagador@teste.local", "Sra. Teste", null, null, 0, null, null, null, null, true, null),
                aceitarNuitDuplicado: true);

            var db = sp.GetRequiredService<AppDbContext>();
            async Task<int> Serie(TipoDocumento tipo)
            {
                var codigo = "R" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
                var r = await sp.GetRequiredService<ServicoSeries>().CriarAsync(new NovaSerie(tipo, codigo, Hoje.Year, new DateOnly(Hoje.Year, 1, 1)));
                Assert.True(r.Sucesso, string.Join("; ", r.Erros));
                return (await db.Series.SingleAsync(s => s.Codigo == codigo && s.TipoDoc == tipo)).Id;
            }
            return (cliente.Id!.Value, await Serie(TipoDocumento.FT), await Serie(TipoDocumento.NC), await Serie(TipoDocumento.RC));
        });
    }

    /// <summary>Emite uma factura (Gestor) e devolve o id. Total = valor × 1,16.</summary>
    private Task<int> FacturaAsync(int cliente, int serie, decimal semIva, DateOnly? data = null, DateOnly? vencimento = null) =>
        ComoAsync(PerfisPadrao.Gestor, async sp =>
        {
            var d = data ?? Hoje;
            var (r, id) = await sp.GetRequiredService<ServicoDocumentos>().GuardarRascunhoAsync(null, new DadosRascunho(
                TipoDocumento.FT, serie, cliente, d, vencimento ?? d.AddDays(30), "MZN", 1, 0, null, null, null, null, null, null, null, null,
                [new LinhaRascunho(null, null, "Serviço", "un", 1, semIva, 0, "NOR")]));
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));
            var e = await sp.GetRequiredService<ServicoEmissao>().EmitirAsync(id!.Value);
            Assert.Equal(ResultadoEmissaoTipo.Emitido, e.Tipo);
            return id.Value;
        });

    private Task<(Bezix.Facturacao.Application.Comum.Resultado Resultado, int? Id)> ReciboAsync(int cliente, decimal valor, params Alocacao[] alocacoes) =>
        ComoAsync(PerfisPadrao.Tesouraria, sp => sp.GetRequiredService<ServicoRecibos>().RegistarAsync(
            new DadosRecibo(cliente, Hoje, valor, MetodoPagamento.Transferencia, "TRF-123", null, alocacoes)));

    // ------------------------------------------------------------------

    [Fact]
    public async Task Um_pagamento_alocado_a_tres_facturas_fecha_as_tres_e_o_extracto_bate_certo()
    {
        var (cliente, ft, _, _) = await PrepararAsync();
        var a = await FacturaAsync(cliente, ft, 1_000);   // 1.160
        var b = await FacturaAsync(cliente, ft, 2_000);   // 2.320
        var c = await FacturaAsync(cliente, ft, 500);     //   580

        var (r, reciboId) = await ReciboAsync(cliente, 4_060, new(a, 1_160), new(b, 2_320), new(c, 580));
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));

        await ComoAsync<object?>(PerfisPadrao.Tesouraria, async sp =>
        {
            var cc = sp.GetRequiredService<ServicoContaCorrente>();
            var situacao = await cc.SituacaoAsync([a, b, c]);
            Assert.All(situacao.Values, s => Assert.Equal((EstadoPagamento.Pago, 0m), (s.Estado, s.EmAberto)));
            Assert.Empty(await cc.EmAbertoAsync(cliente));

            var saldo = await cc.SaldoAsync(cliente);
            Assert.Equal((4_060m, 4_060m, 0m, 0m), (saldo.Debitos, saldo.Creditos, saldo.Saldo, saldo.AdiantamentoPorAlocar));

            var extracto = (await cc.ExtractoAsync(cliente, Hoje.AddDays(-1), Hoje))!;
            Assert.Equal(0m, extracto.SaldoInicial);
            Assert.Equal(4, extracto.Movimentos.Count);
            Assert.Equal((4_060m, 4_060m, 0m), (extracto.TotalDebitos, extracto.TotalCreditos, extracto.SaldoFinal));
            Assert.Equal(0m, extracto.Movimentos[^1].Saldo);

            var recibo = await sp.GetRequiredService<AppDbContext>().Documentos.AsNoTracking().SingleAsync(d => d.Id == reciboId);
            Assert.Equal((TipoDocumento.RC, EstadoDocumento.Emitido, 4_060m, "MZN"), (recibo.TipoDoc, recibo.Estado, recibo.Total, recibo.Moeda));
            Assert.Equal(HashDocumento.Calcular(recibo), recibo.Hash);
            Assert.NotNull(recibo.PdfPath);
            return null;
        });
    }

    [Fact]
    public async Task Excesso_fica_como_adiantamento_e_pode_ser_alocado_depois()
    {
        var (cliente, ft, _, _) = await PrepararAsync();
        var a = await FacturaAsync(cliente, ft, 1_000);   // 1.160

        var (r, reciboId) = await ReciboAsync(cliente, 2_000, new Alocacao(a, 1_160));
        Assert.True(r.Sucesso);

        var b = await FacturaAsync(cliente, ft, 500);     // 580
        await ComoAsync<object?>(PerfisPadrao.Tesouraria, async sp =>
        {
            var cc = sp.GetRequiredService<ServicoContaCorrente>();
            var recibos = sp.GetRequiredService<ServicoRecibos>();
            Assert.Equal(840m, await cc.AdiantamentoAsync(cliente));
            Assert.Equal(580m - 840m, (await cc.SaldoAsync(cliente)).Saldo);

            var pagamento = (await recibos.PagamentoDoReciboAsync(reciboId!.Value))!;
            var demais = await recibos.AlocarAdiantamentoAsync(pagamento.Id, [new Alocacao(b, 900)]);
            Assert.False(demais.Sucesso);

            var ok = await recibos.AlocarAdiantamentoAsync(pagamento.Id, [new Alocacao(b, 580)]);
            Assert.True(ok.Sucesso, string.Join("; ", ok.Erros));
            Assert.Equal(260m, await cc.AdiantamentoAsync(cliente));
            Assert.Equal(EstadoPagamento.Pago, (await cc.SituacaoAsync([b]))[b].Estado);
            return null;
        });
    }

    [Fact]
    public async Task Nao_se_aloca_mais_do_que_o_valor_em_aberto_nem_mais_do_que_o_recebido()
    {
        var (cliente, ft, _, _) = await PrepararAsync();
        var a = await FacturaAsync(cliente, ft, 1_000); // 1.160

        var (acima, _) = await ReciboAsync(cliente, 2_000, new Alocacao(a, 1_500));
        Assert.Contains(acima.Erros, e => e.Contains("só faltam 1.160,00") || e.Contains("só faltam 1,160.00") || e.Contains("só faltam"));

        var (somaMaior, _) = await ReciboAsync(cliente, 500, new Alocacao(a, 600));
        Assert.Contains(somaMaior.Erros, e => e.Contains("passa o valor recebido"));
    }

    [Fact]
    public async Task Estados_por_pagar_parcial_e_vencido_e_nota_de_credito_reduz_o_aberto()
    {
        if (Hoje.DayOfYear < 70) return; // as datas no passado têm de caber no ano da série
        var (cliente, ft, nc, _) = await PrepararAsync();
        var vencida = await FacturaAsync(cliente, ft, 1_000, Hoje.AddDays(-60), Hoje.AddDays(-30));
        var parcial = await FacturaAsync(cliente, ft, 1_000);
        var porPagar = await FacturaAsync(cliente, ft, 1_000);

        await ReciboAsync(cliente, 160, new Alocacao(parcial, 160));
        await ComoAsync<object?>(PerfisPadrao.Gestor, async sp =>
        {
            var (r, ncId) = await sp.GetRequiredService<ServicoDocumentos>().GuardarRascunhoAsync(null, new DadosRascunho(
                TipoDocumento.NC, nc, cliente, Hoje, null, "MZN", 1, 0, null, null, null, null, null, null, porPagar, "Desconto comercial",
                [new LinhaRascunho(null, null, "Desconto", "un", 1, 100, 0, "NOR")]));
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));
            Assert.Equal(ResultadoEmissaoTipo.Emitido, (await sp.GetRequiredService<ServicoEmissao>().EmitirAsync(ncId!.Value)).Tipo);

            var s = await sp.GetRequiredService<ServicoContaCorrente>().SituacaoAsync([vencida, parcial, porPagar]);
            Assert.Equal((EstadoPagamento.Vencido, 30), (s[vencida].Estado, s[vencida].DiasAtraso));
            Assert.Equal((EstadoPagamento.Parcial, 1_000m), (s[parcial].Estado, s[parcial].EmAberto));
            Assert.Equal((EstadoPagamento.Parcial, 1_044m), (s[porPagar].Estado, s[porPagar].EmAberto)); // 1.160 − NC 116

            var antiguidade = (await sp.GetRequiredService<ServicoContaCorrente>().AntiguidadeAsync(Hoje)).Single(l => l.ClienteId == cliente);
            Assert.Equal((2_044m, 1_160m, 0m), (antiguidade.AVencer, antiguidade.Ate30 + antiguidade.De31a60, antiguidade.Mais90));
            Assert.Equal(antiguidade.Saldo, antiguidade.AVencer + antiguidade.Vencido + antiguidade.Creditos);
            return null;
        });
    }

    [Fact]
    public async Task Factura_paga_nao_se_anula_mas_anular_o_recibo_reabre_a_factura()
    {
        var (cliente, ft, _, _) = await PrepararAsync();
        var a = await FacturaAsync(cliente, ft, 1_000);
        var (_, reciboId) = await ReciboAsync(cliente, 1_160, new Alocacao(a, 1_160));

        await ComoAsync<object?>(PerfisPadrao.Gestor, async sp =>
        {
            var emissao = sp.GetRequiredService<ServicoEmissao>();
            var bloqueada = await emissao.AnularAsync(a, "Factura errada");
            Assert.Contains("tem pagamentos", bloqueada.Erros[0]);

            Assert.True((await emissao.AnularAsync(reciboId!.Value, "Transferência devolvida pelo banco")).Sucesso);
            var s = await sp.GetRequiredService<ServicoContaCorrente>().SituacaoAsync([a]);
            Assert.Equal((EstadoPagamento.PorPagar, 1_160m), (s[a].Estado, s[a].EmAberto));
            Assert.True((await emissao.AnularAsync(a, "Factura errada")).Sucesso);
            return null;
        });
    }

    [Fact]
    public async Task Dois_recibos_simultaneos_nao_pagam_duas_vezes_a_mesma_factura()
    {
        var (cliente, ft, _, _) = await PrepararAsync();
        var a = await FacturaAsync(cliente, ft, 1_000); // 1.160

        var resultados = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => ReciboAsync(cliente, 1_160, new Alocacao(a, 1_160)))));

        Assert.Equal(1, resultados.Count(r => r.Resultado.Sucesso));
        await ComoAsync<object?>(PerfisPadrao.Tesouraria, async sp =>
        {
            Assert.Equal(1_160m, await sp.GetRequiredService<AppDbContext>().PagamentoAlocacoes.Where(x => x.DocumentoId == a).SumAsync(x => x.Valor));
            return null;
        });
    }

    [Fact]
    public async Task Pagamentos_nao_se_alteram_por_SQL_e_so_a_tesouraria_e_gestao_registam_recibos()
    {
        var (cliente, ft, _, _) = await PrepararAsync();
        var a = await FacturaAsync(cliente, ft, 1_000);
        var (_, reciboId) = await ReciboAsync(cliente, 1_160, new Alocacao(a, 1_160));

        await ComoAsync<object?>(PerfisPadrao.Administrador, async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"UPDATE PagamentoAlocacoes SET Valor = 1 WHERE DocumentoId = {a}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync($"DELETE FROM Pagamentos WHERE ReciboId = {reciboId}"));
            return null;
        });

        var facturador = await ComoAsync(PerfisPadrao.Facturador, sp => sp.GetRequiredService<ServicoRecibos>()
            .RegistarAsync(new DadosRecibo(cliente, Hoje, 10, MetodoPagamento.Numerario, null, null, [])));
        Assert.Contains("não permite registar recibos", facturador.Resultado.Erros[0]);
    }

    [Fact]
    public async Task PDF_do_recibo_e_extracto_por_email_com_anexo()
    {
        var (cliente, ft, _, _) = await PrepararAsync();
        var a = await FacturaAsync(cliente, ft, 1_000);
        var (_, reciboId) = await ReciboAsync(cliente, 1_500, new Alocacao(a, 1_160));

        await ComoAsync<object?>(PerfisPadrao.Tesouraria, async sp =>
        {
            var pdf = await sp.GetRequiredService<ServicoPdfDocumentos>().ObterAsync(reciboId!.Value);
            Assert.True(pdf!.Conteudo.AsSpan().StartsWith("%PDF"u8));

            var comunicacao = sp.GetRequiredService<ServicoComunicacao>();
            var email = await comunicacao.PrepararExtractoAsync(cliente, Hoje.AddDays(-30), Hoje);
            Assert.Equal("pagador@teste.local", email!.Para);
            Assert.Contains("-340,00 MZN", email.Corpo); // saldo a favor do cliente: 1.160 − 1.500
            var r = await comunicacao.EnviarExtractoAsync(cliente, Hoje.AddDays(-30), Hoje, email);
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));
            return null;
        });

        var eml = Directory.GetFiles(app.PastaEmails).OrderByDescending(File.GetCreationTimeUtc).First();
        var mensagem = await MimeKit.MimeMessage.LoadAsync(eml);
        Assert.StartsWith("Extracto_", Assert.Single(mensagem.Attachments.OfType<MimeKit.MimePart>()).FileName);
    }
}
