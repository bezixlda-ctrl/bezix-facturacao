using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Documentos;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Infrastructure.Persistencia;

public sealed class Transacoes(AppDbContext db) : ITransacoes
{
    public Task<T> ExecutarAsync<T>(Func<CancellationToken, Task<T>> accao, CancellationToken ct = default)
    {
        // Com EnableRetryOnFailure, transacções explícitas têm de correr dentro da estratégia de execução.
        var estrategia = db.Database.CreateExecutionStrategy();
        return estrategia.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // cada tentativa começa com dados frescos
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var resultado = await accao(ct);
            await tx.CommitAsync(ct);
            return resultado;
        });
    }
}

public sealed class BloqueioSerie(AppDbContext db) : IBloqueioSerie
{
    public async Task<Serie> BloquearAsync(int serieId, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("O bloqueio da série só faz sentido dentro de uma transacção.");

        // UPDLOCK: mais ninguém lê a série para escrever até ao commit. HOLDLOCK: o bloqueio dura toda a transacção.
        // Se a série já estava carregada, o EF devolveria os valores antigos: nesse caso recarrega-se (já bloqueada).
        var jaCarregada = db.Series.Local.Any(s => s.Id == serieId);
        var serie = await db.Series
            .FromSql($"SELECT * FROM [Series] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {serieId}")
            .SingleAsync(ct);
        if (jaCarregada) await db.Entry(serie).ReloadAsync(ct);
        return serie;
    }
}
