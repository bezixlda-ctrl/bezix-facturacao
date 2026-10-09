using System.Text.RegularExpressions;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Documentos;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Configuracao;

public sealed record NovaSerie(TipoDocumento TipoDoc, string Codigo, int Ano, DateOnly DataAbertura);

public sealed partial class ServicoSeries(IAppDbContext db, IServicoAuditoria auditoria)
{
    public Task<List<Serie>> ListarAsync(CancellationToken ct = default) =>
        db.Series.AsNoTracking()
            .OrderBy(s => s.Estado)
            .ThenByDescending(s => s.Ano)
            .ThenBy(s => s.TipoDoc)
            .ThenBy(s => s.Codigo)
            .ToListAsync(ct);

    public async Task<Resultado> CriarAsync(NovaSerie dados, CancellationToken ct = default)
    {
        var codigo = (dados.Codigo ?? "").Trim().ToUpperInvariant();
        var erros = new List<string>();
        if (!CodigoValido().IsMatch(codigo)) erros.Add("O código da série só pode ter letras e algarismos (1 a 10), sem espaços. Ex.: A, B, ONLINE.");
        if (dados.Ano is < 2000 or > 2100) erros.Add("Ano inválido.");
        if (dados.DataAbertura.Year != dados.Ano) erros.Add("A data de abertura tem de pertencer ao ano da série.");
        if (await db.Series.AnyAsync(s => s.TipoDoc == dados.TipoDoc && s.Codigo == codigo && s.Ano == dados.Ano, ct))
            erros.Add($"A série {dados.TipoDoc} {codigo}{dados.Ano} já existe.");
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        var serie = new Serie
        {
            TipoDoc = dados.TipoDoc,
            Codigo = codigo,
            Ano = dados.Ano,
            ProximoNumero = 1,
            Estado = EstadoSerie.Activa,
            DataAbertura = dados.DataAbertura,
        };
        db.Series.Add(serie);
        await db.SaveChangesAsync(ct);
        auditoria.Registar(nameof(Serie), serie.Id.ToString(), "Serie.Abrir", null, new { serie.Descricao, serie.DataAbertura });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>Fecha a série (fim do ano ou mudança de taxa). Regista a data e o último número usado.</summary>
    public async Task<Resultado> FecharAsync(int id, DateOnly dataFecho, CancellationToken ct = default)
    {
        var serie = await db.Series.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (serie is null) return Resultado.Falha("Série não encontrada.");
        if (serie.Estado == EstadoSerie.Fechada) return Resultado.Falha("Esta série já está fechada.");
        if (dataFecho < serie.DataAbertura || (serie.UltimaData is { } u && dataFecho < u))
            return Resultado.Falha("A data de fecho não pode ser anterior à abertura nem ao último documento emitido.");

        serie.Estado = EstadoSerie.Fechada;
        serie.DataFecho = dataFecho;
        auditoria.Registar(nameof(Serie), serie.Id.ToString(), "Serie.Fechar", null,
            new { serie.Descricao, UltimoNumero = serie.ProximoNumero - 1, dataFecho });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    [GeneratedRegex("^[A-Z0-9]{1,10}$")]
    private static partial Regex CodigoValido();
}
