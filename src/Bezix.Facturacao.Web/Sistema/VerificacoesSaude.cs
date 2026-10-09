using Microsoft.EntityFrameworkCore;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Bezix.Facturacao.Infrastructure.Servicos;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bezix.Facturacao.Web.Sistema;

public sealed class VerificacaoBaseDados(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Sem ligação à base de dados.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Erro ao ligar à base de dados.", ex);
        }
    }
}

public sealed class VerificacaoDisco(ArmazenamentoLocal ficheiros) : IHealthCheck
{
    private const long MinimoLivre = 200L * 1024 * 1024;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var teste = Path.Combine(ficheiros.Raiz, ".health");
            File.WriteAllText(teste, DateTime.UtcNow.ToString("O"));
            File.Delete(teste);

            // Em alojamento partilhado a quota pode não ser visível; o espaço livre é só informativo.
            var raiz = Path.GetPathRoot(ficheiros.Raiz);
            var livre = raiz is null ? (long?)null : new DriveInfo(raiz).AvailableFreeSpace;
            return Task.FromResult(livre is < MinimoLivre
                ? HealthCheckResult.Degraded($"Pouco espaço livre: {livre / 1024 / 1024} MB.")
                : HealthCheckResult.Healthy());
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("A pasta de ficheiros não aceita escrita.", ex));
        }
    }
}

/// <summary>O Scheduled Task Tool tem de chamar o jobs/tick: sem execução há mais de 1 hora, lembretes e emails param.</summary>
public sealed class VerificacaoTarefas(AppDbContext db, TimeProvider relogio) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var ultima = await db.ExecucoesTarefas.AsNoTracking().Where(e => e.Tarefa != Infrastructure.Backups.ServicoBackups.Tarefa)
            .OrderByDescending(e => e.Id).Select(e => (DateTime?)e.Inicio).FirstOrDefaultAsync(ct);
        if (ultima is null) return HealthCheckResult.Degraded("As tarefas automáticas (jobs/tick) nunca correram.");
        var ha = relogio.GetUtcNow().UtcDateTime - ultima.Value;
        return ha > TimeSpan.FromHours(1)
            ? HealthCheckResult.Degraded($"Última execução do jobs/tick há {(int)ha.TotalMinutes} minutos.")
            : HealthCheckResult.Healthy($"Última execução há {(int)ha.TotalMinutes} min.");
    }
}

/// <summary>Fila de email: emails à espera há mais de 1 hora indicam SMTP em baixo ou mal configurado.</summary>
public sealed class VerificacaoEmails(AppDbContext db, TimeProvider relogio) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var limite = relogio.GetUtcNow().UtcDateTime.AddHours(-1);
        var atrasados = await db.EmailsFila.AsNoTracking().CountAsync(e => e.Estado == Domain.Comunicacao.EstadoEmail.Pendente && e.CreatedAt < limite, ct);
        var falhados = await db.EmailsFila.AsNoTracking().CountAsync(e => e.Estado == Domain.Comunicacao.EstadoEmail.Falhou && e.CreatedAt >= limite.AddHours(-23), ct);
        return atrasados + falhados == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded($"{atrasados} emails na fila há mais de 1 h; {falhados} falhados nas últimas 24 h.");
    }
}

/// <summary>Backups externos configurados e com sucesso nas últimas 30 horas.</summary>
public sealed class VerificacaoBackups(Infrastructure.Backups.ServicoBackups backups,
    Microsoft.Extensions.Options.IOptions<Infrastructure.Backups.OpcoesBackups> opcoes, TimeProvider relogio) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!opcoes.Value.Configurado) return HealthCheckResult.Degraded("Backups externos não configurados.");
        var ultimo = await backups.UltimoSucessoAsync(ct);
        if (ultimo is null) return HealthCheckResult.Degraded("Ainda não houve nenhum backup com sucesso.");
        var ha = relogio.GetUtcNow().UtcDateTime - ultimo.Value;
        return ha > TimeSpan.FromHours(30)
            ? HealthCheckResult.Degraded($"Último backup há {(int)ha.TotalHours} horas.")
            : HealthCheckResult.Healthy($"Último backup há {(int)ha.TotalHours} h.");
    }
}
