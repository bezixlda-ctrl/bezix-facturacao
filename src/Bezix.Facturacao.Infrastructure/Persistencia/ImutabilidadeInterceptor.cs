using System.Text.Json;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Documentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Bezix.Facturacao.Infrastructure.Persistencia;

/// <summary>
/// Primeira barreira de imutabilidade (a segunda são os triggers SQL): recusa qualquer alteração a documentos
/// emitidos, excepto a anulação (estado, motivo, data, utilizador) e o caminho do PDF. A tentativa fica na auditoria.
/// </summary>
public sealed class ImutabilidadeInterceptor(IUtilizadorActual utilizador, TimeProvider relogio, ILogger<ImutabilidadeInterceptor> log) : SaveChangesInterceptor
{
    private static readonly HashSet<string> CamposPermitidos =
    [
        nameof(Documento.Estado), nameof(Documento.MotivoAnulacao), nameof(Documento.AnuladoEm),
        nameof(Documento.AnuladoPor), nameof(Documento.PdfPath), nameof(Documento.UpdatedAt),
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Verificar(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Verificar(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Verificar(DbContext? contexto)
    {
        if (contexto is not AppDbContext db) return;

        foreach (var e in db.ChangeTracker.Entries<Documento>())
        {
            if (e.State is not (EntityState.Modified or EntityState.Deleted)) continue;
            var original = e.Property(d => d.Estado).OriginalValue;
            if (original is not (EstadoDocumento.Emitido or EstadoDocumento.Anulado)) continue;

            if (e.State == EntityState.Deleted) Recusar(db, e.Entity.Id, "apagar");

            var alterados = e.Properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)).Select(p => p.Metadata.Name).ToList();
            var proibidos = alterados.Where(n => !CamposPermitidos.Contains(n)).ToList();
            if (proibidos.Count > 0) Recusar(db, e.Entity.Id, "alterar " + string.Join(", ", proibidos));

            var novo = e.Entity.Estado;
            if (novo != original && !(original == EstadoDocumento.Emitido && novo == EstadoDocumento.Anulado))
                Recusar(db, e.Entity.Id, $"mudar o estado de {original} para {novo}");
            if (original == EstadoDocumento.Anulado && alterados.Any(n => n != nameof(Documento.PdfPath) && n != nameof(Documento.UpdatedAt)))
                Recusar(db, e.Entity.Id, "alterar um documento anulado");
        }

        var filhos = db.ChangeTracker.Entries<DocumentoLinha>().Where(Alterado).Select(e => e.Entity.DocumentoId)
            .Concat(db.ChangeTracker.Entries<DocumentoIvaResumo>().Where(Alterado).Select(e => e.Entity.DocumentoId))
            .Where(id => id > 0)
            .Distinct().ToList();
        foreach (var docId in filhos)
        {
            var tracked = db.ChangeTracker.Entries<Documento>().FirstOrDefault(d => d.Entity.Id == docId);
            var estado = tracked is not null
                ? tracked.Property(d => d.Estado).OriginalValue
                : db.Documentos.AsNoTracking().Where(d => d.Id == docId).Select(d => (EstadoDocumento?)d.Estado).FirstOrDefault();
            if (estado is EstadoDocumento.Emitido or EstadoDocumento.Anulado) Recusar(db, docId, "alterar linhas ou IVA");
        }
    }

    private static bool Alterado(EntityEntry e) => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    private void Recusar(AppDbContext db, int documentoId, string detalhe)
    {
        log.LogWarning("Tentativa de {Detalhe} no documento emitido {Id} por {Utilizador}.", detalhe, documentoId, utilizador.Email);
        try
        {
            // Gravado à parte (fora do SaveChanges recusado) para a tentativa ficar registada.
            var depois = JsonSerializer.Serialize(new { detalhe });
            db.Database.ExecuteSql($@"INSERT INTO [Auditoria] ([Entidade], [EntidadeId], [Accao], [Antes], [Depois], [Utilizador], [Ip], [Data])
                VALUES ({nameof(Documento)}, {documentoId.ToString()}, {"Documento.TentativaAlteracao"}, {null as string}, {depois}, {utilizador.Email ?? "sistema"}, {utilizador.Ip}, {relogio.GetUtcNow().UtcDateTime})");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Não foi possível registar na auditoria a tentativa de alteração do documento {Id}.", documentoId);
        }
        throw new DocumentoImutavelException(documentoId, detalhe);
    }
}
