using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Documentos;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Documentos;

/// <summary>
/// Atribui número, data/hora de emissão e hash encadeado a um documento. Tem de ser chamado dentro de uma
/// transacção, com a série já bloqueada por <see cref="IBloqueioSerie"/>.
/// </summary>
public sealed class NumeradorDocumentos(IAppDbContext db, IUtilizadorActual utilizador, TimeProvider relogio)
{
    public async Task NumerarAsync(Documento doc, Serie serie, CancellationToken ct)
    {
        if (serie.TipoDoc != doc.TipoDoc) throw new InvalidOperationException("A série não corresponde ao tipo de documento.");

        var hashAnterior = await db.Documentos.AsNoTracking()
            .Where(d => d.SerieId == serie.Id && d.Numero != null)
            .OrderByDescending(d => d.Numero)
            .Select(d => d.Hash)
            .FirstOrDefaultAsync(ct);

        var agora = relogio.GetUtcNow().UtcDateTime;
        doc.SerieId = serie.Id;
        doc.Numero = serie.ProximoNumero;
        doc.NumeroFormatado = Serie.FormatarNumero(serie.TipoDoc, serie.Codigo, serie.Ano, serie.ProximoNumero);
        doc.EmitidoEm = new DateTime(agora.Ticks - agora.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        doc.EmitidoPor = utilizador.Email ?? "sistema";
        doc.HashAnterior = hashAnterior;
        doc.Hash = HashDocumento.Calcular(doc);
        doc.Estado = EstadoDocumento.Emitido;

        serie.ProximoNumero++;
        serie.UltimaData = doc.Data;
    }
}
