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
