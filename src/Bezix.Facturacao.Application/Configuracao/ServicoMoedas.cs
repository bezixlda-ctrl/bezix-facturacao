using System.Text.RegularExpressions;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Configuracao;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Configuracao;

public sealed record NovaMoeda(string Codigo, string Nome, string Simbolo, string NomeSingular, string NomePlural, string CentimoSingular, string CentimoPlural);

public sealed partial class ServicoMoedas(IAppDbContext db, IServicoAuditoria auditoria)
{
    public Task<List<Moeda>> ListarAsync(CancellationToken ct = default) =>
        db.Moedas.AsNoTracking().OrderBy(m => m.Ordem).ThenBy(m => m.Codigo).ToListAsync(ct);

    public Task<List<Moeda>> ActivasAsync(CancellationToken ct = default) =>
        db.Moedas.AsNoTracking().Where(m => m.Activa).OrderBy(m => m.Ordem).ThenBy(m => m.Codigo).ToListAsync(ct);

    public async Task<Moeda> ObterAsync(string codigo, CancellationToken ct = default) =>
        await db.Moedas.AsNoTracking().SingleOrDefaultAsync(m => m.Codigo == codigo, ct)
        ?? throw new KeyNotFoundException($"Moeda {codigo} não existe.");

    public async Task<Resultado> CriarAsync(NovaMoeda d, CancellationToken ct = default)
    {
        var codigo = (d.Codigo ?? "").Trim().ToUpperInvariant();
        var erros = new List<string>();
        if (!CodigoIso().IsMatch(codigo)) erros.Add("O código tem de ter 3 letras (ISO 4217, ex.: USD).");
        if (await db.Moedas.AnyAsync(m => m.Codigo == codigo, ct)) erros.Add($"A moeda {codigo} já existe.");
        if (string.IsNullOrWhiteSpace(d.Nome)) erros.Add("Indique o nome.");
        if (string.IsNullOrWhiteSpace(d.NomeSingular) || string.IsNullOrWhiteSpace(d.NomePlural))
            erros.Add("Indique o nome no singular e no plural (usado no valor por extenso).");
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        db.Moedas.Add(new Moeda
        {
            Codigo = codigo,
            Nome = d.Nome.Trim(),
            Simbolo = string.IsNullOrWhiteSpace(d.Simbolo) ? codigo : d.Simbolo.Trim(),
            NomeSingular = d.NomeSingular.Trim(),
            NomePlural = d.NomePlural.Trim(),
            CentimoSingular = string.IsNullOrWhiteSpace(d.CentimoSingular) ? "cêntimo" : d.CentimoSingular.Trim(),
            CentimoPlural = string.IsNullOrWhiteSpace(d.CentimoPlural) ? "cêntimos" : d.CentimoPlural.Trim(),
            Ordem = await db.Moedas.CountAsync(ct) + 1,
        });
        auditoria.Registar(nameof(Moeda), codigo, "Moeda.Criar", null, new { codigo, d.Nome });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> DefinirActivaAsync(string codigo, bool activa, CancellationToken ct = default)
    {
        if (codigo == Moeda.Base && !activa) return Resultado.Falha("O metical é a moeda base e não pode ser desactivado.");
        var m = await db.Moedas.SingleOrDefaultAsync(x => x.Codigo == codigo, ct);
        if (m is null) return Resultado.Falha("Moeda não encontrada.");
        m.Activa = activa;
        auditoria.Registar(nameof(Moeda), codigo, activa ? "Moeda.Activar" : "Moeda.Desactivar");
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    // ---------- Câmbios ----------

    public Task<List<TaxaCambio>> CambiosRecentesAsync(int quantos = 60, CancellationToken ct = default) =>
        db.TaxasCambio.AsNoTracking().OrderByDescending(c => c.Data).ThenBy(c => c.Moeda).Take(quantos).ToListAsync(ct);

    /// <summary>Câmbio mais recente com data igual ou anterior à indicada; 1 para MZN; null se não houver.</summary>
    public async Task<decimal?> CambioAsync(string moeda, DateOnly data, CancellationToken ct = default)
    {
        if (moeda == Moeda.Base) return 1m;
        return await db.TaxasCambio.AsNoTracking()
            .Where(c => c.Moeda == moeda && c.Data <= data)
            .OrderByDescending(c => c.Data)
            .Select(c => (decimal?)c.Valor)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Resultado> RegistarCambioAsync(string moeda, DateOnly data, decimal valor, CancellationToken ct = default)
    {
        if (moeda == Moeda.Base) return Resultado.Falha("O metical é a moeda base: o câmbio é sempre 1.");
        if (!await db.Moedas.AnyAsync(m => m.Codigo == moeda, ct)) return Resultado.Falha("Moeda não encontrada.");
        if (valor <= 0) return Resultado.Falha("O câmbio tem de ser maior que zero.");
        if (decimal.Round(valor, 6) != valor) return Resultado.Falha("O câmbio pode ter no máximo 6 casas decimais.");

        var existente = await db.TaxasCambio.SingleOrDefaultAsync(c => c.Moeda == moeda && c.Data == data, ct);
        if (existente is null) db.TaxasCambio.Add(new TaxaCambio { Moeda = moeda, Data = data, Valor = valor });
        else existente.Valor = valor;
        auditoria.Registar(nameof(TaxaCambio), moeda, "Cambio.Registar", null, new { moeda, data, valor });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CodigoIso();
}
