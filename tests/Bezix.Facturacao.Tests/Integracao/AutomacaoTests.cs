using System.Net;
using System.Text.Json;
using Bezix.Facturacao.Application.Automacao;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Application.Integracoes;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Automacao;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Integracoes;
using Bezix.Facturacao.Domain.Recebimentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bezix.Facturacao.Tests.Integracao;

/// <summary>Fase 5: links para o cliente, pagamentos comunicados, recorrências, modelos de email e webhooks.</summary>
[Collection(ColeccaoAplicacao.Nome)]
public class AutomacaoTests(AplicacaoFactory app)
{
    private static readonly Random Aleatorio = new();
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(2));
    private static readonly byte[] PdfFicticio = "%PDF-1.4\n% comprovativo de teste\n"u8.ToArray();

    private async Task<T> ComoAsync<T>(string perfil, Func<IServiceProvider, Task<T>> accao)
    {
        using var _ = UtilizadorTeste.Como(perfil);
        using var scope = app.Services.CreateScope();
        return await accao(scope.ServiceProvider);
    }

    private Task ComoAsync(string perfil, Func<IServiceProvider, Task> accao) =>
        ComoAsync<object?>(perfil, async sp => { await accao(sp); return null; });

    /// <summary>Cliente novo (com email) e série FT própria deste teste.</summary>
    private Task<(int Cliente, int SerieFt)> PrepararAsync() =>
        ComoAsync(PerfisPadrao.Administrador, async sp =>
        {
            var empresas = sp.GetRequiredService<ServicoEmpresa>();
            var e = await empresas.ObterAsync();
            if (e.Nuit == InicializadorBaseDados.NuitPorPreencher)
                await empresas.ActualizarDadosAsync(new DadosEmpresa(e.Nome, "400111222", e.Slogan, "Av. Teste, 1", e.Cidade, e.Provincia, e.Telefones,
                    e.Email, e.Site, e.CorPrimaria, e.RodapeTexto, e.PrazoPagamentoDias, e.LimiteNuitObrigatorio, e.ModoArredondamentoIva));

            var cliente = await sp.GetRequiredService<ServicoClientes>().GuardarAsync(null, new DadosCliente(
                $"Cliente Auto {Guid.NewGuid():N}"[..26], TipoCliente.Empresa, Aleatorio.Next(400_000_000, 499_999_999).ToString(),
                "Rua 5", "Maputo", null, null, "cliente.auto@teste.local", "Sr. Auto", null, null, 0, null, null, null, null, true, null),
                aceitarNuitDuplicado: true);

            var codigo = "A" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var r = await sp.GetRequiredService<ServicoSeries>().CriarAsync(new NovaSerie(TipoDocumento.FT, codigo, Hoje.Year, new DateOnly(Hoje.Year, 1, 1)));
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));
            var serie = await sp.GetRequiredService<AppDbContext>().Series.SingleAsync(s => s.Codigo == codigo);
            return (cliente.Id!.Value, serie.Id);
        });

    /// <summary>Emite uma factura de 1 linha (Gestor). Total = valor × 1,16.</summary>
    private Task<int> FacturaAsync(int cliente, int serie, decimal semIva) =>
        ComoAsync(PerfisPadrao.Gestor, async sp =>
        {
            var (r, id) = await sp.GetRequiredService<ServicoDocumentos>().GuardarRascunhoAsync(null, new DadosRascunho(
                TipoDocumento.FT, serie, cliente, Hoje, Hoje.AddDays(30), "MZN", 1, 0, null, null, null, null, null, null, null, null,
                [new LinhaRascunho(null, null, "Avença mensal", "un", 1, semIva, 0, "NOR")]));
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));
            var e = await sp.GetRequiredService<ServicoEmissao>().EmitirAsync(id!.Value);
            Assert.Equal(ResultadoEmissaoTipo.Emitido, e.Tipo);
            return id.Value;
        });

    private static string TokenDe(string url) => url[(url.LastIndexOf('/') + 1)..];

    // ------------------------------------------------------------------ Link do cliente

    [Fact]
    public async Task Link_abre_o_documento_regista_a_primeira_visita_e_deixa_de_valer_quando_expira()
    {
        var (cliente, ft) = await PrepararAsync();
        var doc = await FacturaAsync(cliente, ft, 1_000);
        var url = await ComoAsync(PerfisPadrao.Gestor, sp => sp.GetRequiredService<ServicoLinks>().CriarAsync(doc));

        // Página pública, sem sessão.
        var http = app.CriarCliente();
        var pagina = await http.GetAsync($"/p/{TokenDe(url)}");
        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        var html = await pagina.Content.ReadAsStringAsync();
        Assert.Contains("1.160,00 MT", html);
        Assert.Contains("Envie o comprovativo", html);

        var pdf = await http.GetAsync($"/p/{TokenDe(url)}?handler=Pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);

        await ComoAsync(PerfisPadrao.Gestor, async sp =>
        {
            Assert.NotNull(await sp.GetRequiredService<ServicoLinks>().VistoEmAsync(doc));
            var db = sp.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Auditoria.CountAsync(a => a.Accao == "Documento.Visto" && a.EntidadeId == doc.ToString()));

            // Expira: o mesmo link passa a mostrar "já não é válido" (404) e não serve o PDF.
            await db.LinksPartilha.Where(l => l.DocumentoId == doc).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiraEm, DateTime.UtcNow.AddMinutes(-1)));
        });
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/p/{TokenDe(url)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/p/{TokenDe(url)}?handler=Pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/p/token-que-nao-existe-de-todo-1234567890")).StatusCode);
    }

    // ------------------------------------------------------------------ Pagamentos comunicados

    [Fact]
    public async Task Pagamento_comunicado_so_entra_na_conta_corrente_depois_de_a_tesouraria_validar()
    {
        var (cliente, ft) = await PrepararAsync();
        var doc = await FacturaAsync(cliente, ft, 1_000); // 1.160

        await ComoAsync(PerfisPadrao.Gestor, async sp =>
        {
            var links = sp.GetRequiredService<ServicoLinks>();
            var aberto = (await links.AbrirAsync(TokenDe(await links.CriarAsync(doc))))!;

            var semFicheiro = await links.ComunicarPagamentoAsync(aberto, 1_160, Hoje, "TRF-1", null, null, "10.0.0.1");
            Assert.False(semFicheiro.Sucesso);
            var executavel = await links.ComunicarPagamentoAsync(aberto, 1_160, Hoje, "TRF-1", null, "MZ\u0090\0"u8.ToArray(), "10.0.0.1");
            Assert.False(executavel.Sucesso);

            var ok = await links.ComunicarPagamentoAsync(aberto, 1_160, Hoje, "TRF-1", "Pago por transferência", PdfFicticio, "10.0.0.1");
            Assert.True(ok.Sucesso, string.Join("; ", ok.Erros));
        });

        var comunicado = await ComoAsync(PerfisPadrao.Tesouraria, async sp =>
        {
            // Ainda nada na conta corrente.
            Assert.Equal(1_160m, (await sp.GetRequiredService<ServicoContaCorrente>().SituacaoAsync([doc]))[doc].EmAberto);
            var lista = await sp.GetRequiredService<ServicoPagamentosComunicados>().ListarAsync(soPorValidar: true);
            var p = Assert.Single(lista, x => x.DocumentoId == doc);
            Assert.True(p.TemComprovativo);
            return p.Id;
        });

        // O Facturador não pode validar pagamentos comunicados.
        var recusado = await ComoAsync(PerfisPadrao.Facturador, sp => sp.GetRequiredService<ServicoRecibos>().RegistarAsync(
            new DadosRecibo(cliente, Hoje, 1_160, MetodoPagamento.Transferencia, "TRF-1", null, [new Alocacao(doc, 1_160)], comunicado)));
        Assert.False(recusado.Resultado.Sucesso);

        var (r, reciboId) = await ComoAsync(PerfisPadrao.Tesouraria, sp => sp.GetRequiredService<ServicoRecibos>().RegistarAsync(
            new DadosRecibo(cliente, Hoje, 1_160, MetodoPagamento.Transferencia, "TRF-1", null, [new Alocacao(doc, 1_160)], comunicado)));
        Assert.True(r.Sucesso, string.Join("; ", r.Erros));

        await ComoAsync(PerfisPadrao.Tesouraria, async sp =>
        {
            Assert.Equal(EstadoPagamento.Pago, (await sp.GetRequiredService<ServicoContaCorrente>().SituacaoAsync([doc]))[doc].Estado);
            var p = (await sp.GetRequiredService<ServicoPagamentosComunicados>().ObterAsync(comunicado))!;
            Assert.Equal((EstadoPagamentoComunicado.Validado, reciboId), (p.Estado, p.ReciboId));

            // Validado uma vez: não volta a servir para outro recibo.
            var (outra, _) = await sp.GetRequiredService<ServicoRecibos>().RegistarAsync(
                new DadosRecibo(cliente, Hoje, 10, MetodoPagamento.Transferencia, null, null, [], comunicado));
            Assert.False(outra.Sucesso);
        });
    }

    [Fact]
    public async Task Pagamento_comunicado_rejeitado_fica_registado_com_o_motivo()
    {
        var (cliente, ft) = await PrepararAsync();
        var doc = await FacturaAsync(cliente, ft, 500);
        await ComoAsync(PerfisPadrao.Gestor, async sp =>
        {
            var links = sp.GetRequiredService<ServicoLinks>();
            var aberto = (await links.AbrirAsync(TokenDe(await links.CriarAsync(doc))))!;
            Assert.True((await links.ComunicarPagamentoAsync(aberto, 580, Hoje, null, null, PdfFicticio, null)).Sucesso);
        });

        await ComoAsync(PerfisPadrao.Tesouraria, async sp =>
        {
            var servico = sp.GetRequiredService<ServicoPagamentosComunicados>();
            var id = (await servico.ListarAsync(true)).Single(x => x.DocumentoId == doc).Id;
            Assert.False((await servico.RejeitarAsync(id, " ")).Sucesso);
            Assert.True((await servico.RejeitarAsync(id, "Não consta no extracto")).Sucesso);
            var p = (await servico.ObterAsync(id))!;
            Assert.Equal((EstadoPagamentoComunicado.Rejeitado, "Não consta no extracto"), (p.Estado, p.MotivoRejeicao));
            Assert.Equal(580m, (await sp.GetRequiredService<ServicoContaCorrente>().SituacaoAsync([doc]))[doc].EmAberto);
        });
    }

    // ------------------------------------------------------------------ Recorrências e tarefas

    [Fact]
    public async Task Factura_recorrente_mensal_e_emitida_no_dia_pelo_tick_e_so_uma_vez()
    {
        var (cliente, ft) = await PrepararAsync();
        var modelo = await FacturaAsync(cliente, ft, 2_500); // 2.900

        var recorrencia = await ComoAsync(PerfisPadrao.Gestor, async sp =>
        {
            var (r, id) = await sp.GetRequiredService<ServicoRecorrencias>().CriarAsync(modelo,
                new DadosRecorrencia("Avença mensal", Periodicidade.Mensal, Hoje, null, EmitirAutomaticamente: true, EnviarEmail: true));
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));
            return id!.Value;
        });

        var http = app.CriarCliente();
        async Task TickAsync()
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post, "/internal/jobs/tick");
            pedido.Headers.Add("X-Jobs-Token", AplicacaoFactory.TokenJobs);
            var resposta = await http.SendAsync(pedido);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }
        await TickAsync();
        await TickAsync();

        await ComoAsync(PerfisPadrao.Gestor, async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var r = await db.Recorrencias.AsNoTracking().SingleAsync(x => x.Id == recorrencia);
            Assert.Null(r.UltimoErro);
            Assert.Equal(Hoje.AddMonths(1), r.ProximaData);

            var gerados = await db.Documentos.AsNoTracking().Where(d => d.ClienteId == cliente && d.Id != modelo).ToListAsync();
            var novo = Assert.Single(gerados);
            Assert.Equal(r.UltimoDocumentoId, novo.Id);
            Assert.Equal((TipoDocumento.FT, EstadoDocumento.Emitido, 2_900m, Hoje), (novo.TipoDoc, novo.Estado, novo.Total, novo.Data));

            var email = await db.EmailsFila.AsNoTracking().SingleAsync(e => e.DocumentoId == novo.Id);
            Assert.Equal(("cliente.auto@teste.local", ServicoRecorrencias.ModeloEmail), (email.Para, email.ModeloCodigo));
            Assert.Contains(await db.ExecucoesTarefas.AsNoTracking().Where(e => e.Tarefa == "Recorrentes").ToListAsync(), e => e.Sucesso);

            // Feito em nome do sistema.
            Assert.Equal("sistema", novo.CreatedBy);
        });
    }

    [Fact]
    public async Task Tick_sem_token_nao_existe()
    {
        var r = await app.CriarCliente().PostAsync("/internal/jobs/tick", null);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // ------------------------------------------------------------------ Modelos de email

    [Fact]
    public async Task Alterar_o_modelo_muda_o_proximo_email_e_variaveis_desconhecidas_sao_recusadas()
    {
        var (cliente, ft) = await PrepararAsync();
        var doc = await FacturaAsync(cliente, ft, 100);
        const string codigo = "documento.FT";

        await ComoAsync(PerfisPadrao.Facturador, async sp =>
            Assert.False((await sp.GetRequiredService<ServicoComunicacao>().GuardarModeloAsync(codigo, "X", "Y", null, true, true)).Sucesso));

        await ComoAsync(PerfisPadrao.Gestor, async sp =>
        {
            var com = sp.GetRequiredService<ServicoComunicacao>();
            var errado = await com.GuardarModeloAsync(codigo, "Factura {documento.numro}", "Olá", null, true, true);
            Assert.False(errado.Sucesso);
            Assert.Contains(errado.Erros, e => e.Contains("{documento.numro}"));

            try
            {
                var ok = await com.GuardarModeloAsync(codigo, "Nova factura {documento.numero} de {empresa.nome}", "Caro {cliente.contacto},\n\nTotal: {documento.total}.", null, true, true);
                Assert.True(ok.Sucesso, string.Join("; ", ok.Erros));

                var email = (await com.PrepararDocumentoAsync(doc))!;
                var numero = (await sp.GetRequiredService<ServicoDocumentos>().ObterAsync(doc))!.NumeroFormatado;
                Assert.StartsWith($"Nova factura {numero} de ", email.Assunto);
                Assert.Contains("Caro Sr. Auto,", email.Corpo);
                Assert.Contains("116,00", email.Corpo);
            }
            finally
            {
                Assert.True((await com.ReporModeloAsync(codigo)).Sucesso);
            }
            Assert.DoesNotContain("Nova factura", (await com.PrepararDocumentoAsync(doc))!.Assunto);
        });
    }

    // ------------------------------------------------------------------ Webhooks

    private async Task<(int Id, string Segredo, string Url)> SubscricaoAsync(params string[] eventos)
    {
        var url = $"https://destino.teste/{Guid.NewGuid():N}";
        return await ComoAsync(PerfisPadrao.Administrador, async sp =>
        {
            var (r, segredo) = await sp.GetRequiredService<ServicoWebhooks>().CriarAsync("Teste", url, eventos);
            Assert.True(r.Sucesso, string.Join("; ", r.Erros));
            var id = await sp.GetRequiredService<AppDbContext>().WebhookSubscricoes.Where(s => s.Url == url).Select(s => s.Id).SingleAsync();
            return (id, segredo!, url);
        });
    }

    /// <summary>A entrega é feita em segundo plano (ProcessadorFilas); espera até chegar ao destino falso.</summary>
    private async Task<IReadOnlyList<EnviadorWebhookFalso.Pedido>> EsperarPedidosAsync(string url, int quantos)
    {
        for (var i = 0; i < 100; i++)
        {
            var pedidos = app.Webhooks.Para(url);
            if (pedidos.Count >= quantos) return pedidos;
            await ComoAsync(PerfisPadrao.Administrador, sp => sp.GetRequiredService<ServicoWebhooks>().EntregarPendentesAsync());
            await Task.Delay(100);
        }
        throw new TimeoutException($"O webhook para {url} não chegou.");
    }

    [Fact]
    public async Task Webhook_e_assinado_repetido_quando_falha_e_entregue_depois()
    {
        var (subscricao, segredo, url) = await SubscricaoAsync(EventosWebhook.DocumentoEmitido);
        app.Webhooks.Responder(url, 500);

        var (cliente, ft) = await PrepararAsync();
        var doc = await FacturaAsync(cliente, ft, 300);

        var pedido = (await EsperarPedidosAsync(url, 1))[0];
        Assert.Equal(EventosWebhook.DocumentoEmitido, pedido.Cabecalhos["X-Evento"]);
        var esperado = "sha256=" + ServicoWebhooks.Assinar(segredo, pedido.Cabecalhos["X-Timestamp"] + "." + pedido.Corpo);
        Assert.Equal(esperado, pedido.Cabecalhos["X-Signature"]);
        using (var json = JsonDocument.Parse(pedido.Corpo))
        {
            Assert.Equal(EventosWebhook.DocumentoEmitido, json.RootElement.GetProperty("evento").GetString());
            Assert.Equal(doc, json.RootElement.GetProperty("dados").GetProperty("id").GetInt32());
        }

        await ComoAsync(PerfisPadrao.Administrador, async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var e = await db.WebhookEntregas.AsNoTracking().SingleAsync(x => x.SubscricaoId == subscricao);
            Assert.Equal((EstadoEntrega.Pendente, 1, 500), (e.Estado, e.Tentativas, e.CodigoResposta));
            Assert.InRange(e.ProximaTentativa!.Value, DateTime.UtcNow.AddSeconds(30), DateTime.UtcNow.AddMinutes(2));

            // O destino recupera; chega a hora da 2.ª tentativa.
            app.Webhooks.Responder(url, 200);
            await db.WebhookEntregas.Where(x => x.Id == e.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ProximaTentativa, DateTime.UtcNow.AddSeconds(-1)));
        });

        await EsperarPedidosAsync(url, 2);
        await ComoAsync(PerfisPadrao.Administrador, async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            WebhookEntrega? e = null;
            for (var i = 0; i < 50 && e?.Estado != EstadoEntrega.Entregue; i++)
            {
                e = await db.WebhookEntregas.AsNoTracking().SingleAsync(x => x.SubscricaoId == subscricao);
                await Task.Delay(50);
            }
            Assert.Equal((EstadoEntrega.Entregue, 2), (e!.Estado, e.Tentativas));
        });
    }

    [Fact]
    public async Task Webhook_e_desactivado_depois_de_5_entregas_falhadas_seguidas()
    {
        var (subscricao, _, url) = await SubscricaoAsync("*");
        app.Webhooks.Responder(url, 503);

        await ComoAsync(PerfisPadrao.Administrador, async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            // 4 entregas já falharam por completo; esta está na última tentativa.
            await db.WebhookSubscricoes.Where(s => s.Id == subscricao).ExecuteUpdateAsync(s => s.SetProperty(x => x.FalhasSeguidas, 4));
            db.WebhookEntregas.Add(new WebhookEntrega
            {
                SubscricaoId = subscricao, EventoId = Guid.NewGuid(), Evento = EventosWebhook.Teste, Payload = "{}",
                Tentativas = ServicoWebhooks.Esperas.Length, ProximaTentativa = DateTime.UtcNow.AddSeconds(-1), CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        await EsperarPedidosAsync(url, 1);
        await ComoAsync(PerfisPadrao.Administrador, async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var s = await db.WebhookSubscricoes.AsNoTracking().SingleAsync(x => x.Id == subscricao);
            Assert.False(s.Activa);
            Assert.Contains("5 entregas seguidas falhadas", s.MotivoDesactivacao);
            Assert.Equal(EstadoEntrega.Falhou, (await db.WebhookEntregas.AsNoTracking().SingleAsync(x => x.SubscricaoId == subscricao)).Estado);
        });
    }

    [Fact]
    public async Task Webhook_so_aceita_enderecos_https()
    {
        await ComoAsync(PerfisPadrao.Administrador, async sp =>
        {
            var (r, segredo) = await sp.GetRequiredService<ServicoWebhooks>().CriarAsync("Inseguro", "http://destino.teste/x", ["*"]);
            Assert.False(r.Sucesso);
            Assert.Null(segredo);
        });
    }
}
