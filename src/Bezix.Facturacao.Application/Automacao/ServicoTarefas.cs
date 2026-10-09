using System.Diagnostics;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Integracoes;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Automacao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Integracoes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bezix.Facturacao.Application.Automacao;

public sealed record ResultadoTarefa(string Tarefa, bool Sucesso, string Resultado, long Milissegundos);

/// <summary>
/// Tarefas automáticas do jobs/tick (o Winhost não tem processos permanentes): emails por repetir, facturas
/// recorrentes, lembretes de cobrança, eventos "documento vencido", webhooks e limpezas. Correm em nome do
/// sistema; cada execução fica no histórico (ver decisão D18 sobre o Hangfire).
/// </summary>
public sealed class ServicoTarefas(
    IAppDbContext db,
    IContextoSistema sistema,
    ServicoComunicacao comunicacao,
    ServicoRecorrencias recorrencias,
    ServicoLembretes lembretes,
    ServicoWebhooks webhooks,
    ServicoContaCorrente contaCorrente,
    IArmazenamentoFicheiros ficheiros,
    TimeProvider relogio,
    ILogger<ServicoTarefas> log)
{
    public async Task<List<ResultadoTarefa>> ExecutarAsync(CancellationToken ct = default)
    {
        using var _ = sistema.Entrar();
        var resultados = new List<ResultadoTarefa>
        {
            await CorrerAsync("Emails", async () => $"{await comunicacao.ProcessarPendentesAsync(ct)} tentados", ct),
            await CorrerAsync("Recorrentes", async () => $"{await recorrencias.ProcessarAsync(ct)} documentos gerados", ct),
            await CorrerAsync("Lembretes", async () => $"{await lembretes.ProcessarAsync(ct)} enviados", ct),
            await CorrerAsync("Vencidos", async () => $"{await PublicarVencidosAsync(ct)} eventos", ct),
            await CorrerAsync("Webhooks", async () => $"{await webhooks.EntregarPendentesAsync(ct)} entregas tentadas", ct),
            await CorrerAsync("Limpeza", () => LimparAsync(ct), ct),
        };
        return resultados;
    }

    public Task<List<ExecucaoTarefa>> HistoricoAsync(int maximo = 50, CancellationToken ct = default) =>
        db.ExecucoesTarefas.AsNoTracking().OrderByDescending(e => e.Id).Take(maximo).ToListAsync(ct);

    private async Task<ResultadoTarefa> CorrerAsync(string nome, Func<Task<string>> tarefa, CancellationToken ct)
    {
        var inicio = relogio.GetUtcNow().UtcDateTime;
        var cronometro = Stopwatch.StartNew();
        string resultado;
        string? erro = null;
        try { resultado = await tarefa(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Falha na tarefa automática {Tarefa}.", nome);
            resultado = "Falhou";
            db.DescartarAlteracoes();
            erro = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
        }

        // Cada tarefa grava o seu próprio trabalho; aqui só se acrescenta o registo da execução.
        db.ExecucoesTarefas.Add(new ExecucaoTarefa
        {
            Tarefa = nome, Inicio = inicio, Fim = relogio.GetUtcNow().UtcDateTime, Sucesso = erro is null, Resultado = resultado, Erro = erro,
        });
        try { await db.SaveChangesAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Não foi possível gravar o histórico da tarefa {Tarefa}.", nome); }
        return new ResultadoTarefa(nome, erro is null, erro ?? resultado, cronometro.ElapsedMilliseconds);
    }

    /// <summary>Evento documento.vencido, uma única vez por documento, quando passa o vencimento com valor em aberto.</summary>
    private async Task<int> PublicarVencidosAsync(CancellationToken ct)
    {
        var hoje = contaCorrente.Hoje();
        var candidatos = await db.Documentos.AsNoTracking()
            .Where(d => d.Estado == EstadoDocumento.Emitido && d.Vencimento != null && d.Vencimento < hoje && d.Vencimento >= hoje.AddDays(-60)
                && (d.TipoDoc == TipoDocumento.FT || d.TipoDoc == TipoDocumento.FG || d.TipoDoc == TipoDocumento.ND))
            .Select(d => new { d.Id, d.NumeroFormatado, d.ClienteId, d.ClienteNome, d.Vencimento, d.Total })
            .ToListAsync(ct);
        var chaves = candidatos.Select(c => $"vencido:{c.Id}").ToList();
        var feitos = (await db.MarcasUnicas.AsNoTracking().Where(m => chaves.Contains(m.Chave)).Select(m => m.Chave).ToListAsync(ct)).ToHashSet();
        var porFazer = candidatos.Where(c => !feitos.Contains($"vencido:{c.Id}")).ToList();
        if (porFazer.Count == 0) return 0;

        var situacao = await contaCorrente.SituacaoAsync(porFazer.Select(c => c.Id).ToList(), ct);
        var n = 0;
        foreach (var c in porFazer)
        {
            if (!situacao.TryGetValue(c.Id, out var s) || s.EmAberto <= 0) continue;
            db.MarcasUnicas.Add(new MarcaUnica { Chave = $"vencido:{c.Id}", CreatedAt = relogio.GetUtcNow().UtcDateTime });
            webhooks.Publicar(EventosWebhook.DocumentoVencido, new
            {
                id = c.Id, numero = c.NumeroFormatado, clienteId = c.ClienteId, clienteNome = c.ClienteNome,
                vencimento = c.Vencimento, total = c.Total, emAberto = s.EmAberto, diasAtraso = s.DiasAtraso,
            });
            n++;
        }
        await db.SaveChangesAsync(ct);
        return n;
    }

    private async Task<string> LimparAsync(CancellationToken ct)
    {
        var importacoes = ficheiros.ApagarAntigos("importacoes", TimeSpan.FromHours(24));
        var limite = relogio.GetUtcNow().UtcDateTime.AddDays(-7);
        var pedidos = await db.PedidosIdempotentes.Where(p => p.CreatedAt < limite).Take(1000).ToListAsync(ct);
        db.PedidosIdempotentes.RemoveRange(pedidos);
        var historico = await db.ExecucoesTarefas.Where(e => e.Inicio < relogio.GetUtcNow().UtcDateTime.AddDays(-90)).Take(1000).ToListAsync(ct);
        db.ExecucoesTarefas.RemoveRange(historico);
        await db.SaveChangesAsync(ct);
        return $"{importacoes} importações abandonadas, {pedidos.Count} chaves de idempotência, {historico.Count} registos antigos";
    }
}
