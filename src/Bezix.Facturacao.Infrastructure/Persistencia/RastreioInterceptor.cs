using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Comum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bezix.Facturacao.Infrastructure.Persistencia;

/// <summary>Preenche CreatedAt, CreatedBy e UpdatedAt (sempre em UTC) em todas as entidades rastreáveis.</summary>
public sealed class RastreioInterceptor(IUtilizadorActual utilizador, TimeProvider relogio) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Preencher(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Preencher(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Preencher(DbContext? contexto)
    {
        if (contexto is null) return;
        var agora = relogio.GetUtcNow().UtcDateTime;
        foreach (var entrada in contexto.ChangeTracker.Entries<IRastreavel>())
        {
            if (entrada.State == EntityState.Added)
            {
                entrada.Entity.CreatedAt = agora;
                entrada.Entity.CreatedBy ??= utilizador.Email ?? "sistema";
            }
            else if (entrada.State == EntityState.Modified)
            {
                entrada.Property(e => e.CreatedAt).IsModified = false;
                entrada.Property(e => e.CreatedBy).IsModified = false;
                entrada.Entity.UpdatedAt = agora;
            }
        }
    }
}
