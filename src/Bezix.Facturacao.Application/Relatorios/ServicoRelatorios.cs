using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Relatorios;

public enum AgruparVendas { Mes, Cliente, Artigo, Tipo }

public sealed record LinhaVendas(string Chave, string Descricao, int Documentos, decimal Quantidade, decimal Base, decimal Iva, decimal Total);

public sealed record LinhaMapaIva(decimal TaxaPct, string? MotivoIsencao, decimal BaseFacturada, decimal IvaFacturado, decimal BaseCreditada, decimal IvaCreditado)
{
    public decimal Base => BaseFacturada - BaseCreditada;
    public decimal Iva => IvaFacturado - IvaCreditado;
}

public sealed record LinhaAnulado(int Id, TipoDocumento Tipo, string Numero, DateOnly Data, string Cliente, decimal Total, DateTime AnuladoEm, string? AnuladoPor, string? Motivo);

/// <summary>
/// Relatórios de vendas e fiscais, sempre sobre documentos emitidos (os anulados não contam) e em meticais.
/// Vendas = FT, FR, FG e ND menos NC. Quem não tem "Ver todos" vê só os documentos de que é responsável.
/// </summary>
public sealed class ServicoRelatorios(IAppDbContext db, IUtilizadorActual utilizador)
{
    private static readonly TipoDocumento[] TiposVenda = [TipoDocumento.FT, TipoDocumento.FR, TipoDocumento.FG, TipoDocumento.ND, TipoDocumento.NC];

    public bool SoProprios => !utilizador.TemPermissao(Permissoes.Documentos.VerTodos);

    private IQueryable<Documento> Vendas(DateOnly de, DateOnly ate)
    {
        var q = db.Documentos.AsNoTracking()
            .Where(d => TiposVenda.Contains(d.TipoDoc) && d.Estado == EstadoDocumento.Emitido && d.Data >= de && d.Data <= ate);
        if (SoProprios)
        {
            var id = utilizador.Id;
            q = q.Where(d => d.ResponsavelId == id);
        }
        return q;
    }

    public async Task<List<LinhaVendas>> VendasAsync(DateOnly de, DateOnly ate, AgruparVendas agrupar, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Relatorios.Vendas)) return [];

        if (agrupar == AgruparVendas.Artigo)
        {
            // Por artigo: base da linha com o desconto global repartido; NC a subtrair.
            var linhas = await (from d in Vendas(de, ate)
                                from l in d.Linhas
                                select new
                                {
                                    d.Id, d.TipoDoc, d.DescontoGlobalPct, l.ArtigoId, l.Ref, l.Descricao, l.Quantidade, l.Base, l.TaxaIvaPct,
                                }).ToListAsync(ct);
            return linhas
                .GroupBy(l => l.ArtigoId is null ? "—" + (l.Ref ?? l.Descricao) : l.ArtigoId.Value.ToString())
                .Select(g =>
                {
                    decimal Sinal(TipoDocumento t) => t == TipoDocumento.NC ? -1 : 1;
                    var bases = g.Select(l => (l, Base: Math.Round(l.Base * (1 - l.DescontoGlobalPct / 100m), 2) * Sinal(l.TipoDoc))).ToList();
                    var b = bases.Sum(x => x.Base);
                    var iva = bases.Sum(x => Math.Round(x.Base * x.l.TaxaIvaPct / 100m, 2));
                    var primeiro = g.First();
                    return new LinhaVendas(primeiro.Ref ?? "", primeiro.Descricao, g.Select(l => l.Id).Distinct().Count(),
                        g.Sum(l => l.Quantidade * Sinal(l.TipoDoc)), b, iva, b + iva);
                })
                .OrderByDescending(l => l.Base).ToList();
        }

        var docs = await Vendas(de, ate)
            .Select(d => new { d.TipoDoc, d.Data, d.ClienteId, d.ClienteNome, d.ClienteNuit, d.BaseTributavel, d.TotalIva, d.Total })
            .ToListAsync(ct);
        var sinalizados = docs.Select(d => (d.TipoDoc, d.Data, d.ClienteId, d.ClienteNome, d.ClienteNuit,
            Base: d.TipoDoc == TipoDocumento.NC ? -d.BaseTributavel : d.BaseTributavel,
            Iva: d.TipoDoc == TipoDocumento.NC ? -d.TotalIva : d.TotalIva,
            Total: d.TipoDoc == TipoDocumento.NC ? -d.Total : d.Total)).ToList();

        IEnumerable<LinhaVendas> r = agrupar switch
        {
            AgruparVendas.Mes => sinalizados.GroupBy(d => new DateOnly(d.Data.Year, d.Data.Month, 1)).OrderBy(g => g.Key)
                .Select(g => Linha(g.Key.ToString("yyyy-MM"), g.Key.ToString("MM/yyyy"), g)),
            AgruparVendas.Cliente => sinalizados.GroupBy(d => d.ClienteId)
                .Select(g => Linha(g.First().ClienteNuit ?? "", g.OrderByDescending(x => x.Data).First().ClienteNome, g))
                .OrderByDescending(l => l.Total),
            _ => sinalizados.GroupBy(d => d.TipoDoc).OrderBy(g => g.Key)
                .Select(g => Linha(g.Key.ToString(), TipoDocumentoInfo.Nome(g.Key), g)),
        };
        return r.ToList();

        static LinhaVendas Linha(string chave, string descricao, IEnumerable<(TipoDocumento TipoDoc, DateOnly Data, int ClienteId, string ClienteNome, string? ClienteNuit, decimal Base, decimal Iva, decimal Total)> g)
        {
            var l = g.ToList();
            return new LinhaVendas(chave, descricao, l.Count, 0, l.Sum(x => x.Base), l.Sum(x => x.Iva), l.Sum(x => x.Total));
        }
    }

    /// <summary>IVA liquidado por taxa no período: facturado (FT, FR, FG, ND) menos creditado (NC).</summary>
    public async Task<List<LinhaMapaIva>> MapaIvaAsync(DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Relatorios.Fiscal)) return [];
        var linhas = await (from d in db.Documentos.AsNoTracking()
                            where TiposVenda.Contains(d.TipoDoc) && d.Estado == EstadoDocumento.Emitido && d.Data >= de && d.Data <= ate
                            from r in d.IvaResumo
                            select new { d.TipoDoc, r.TaxaPct, r.MotivoIsencao, r.BaseMzn, r.IvaMzn }).ToListAsync(ct);
        return linhas.GroupBy(l => (l.TaxaPct, l.MotivoIsencao))
            .Select(g => new LinhaMapaIva(g.Key.TaxaPct, g.Key.MotivoIsencao,
                g.Where(x => x.TipoDoc != TipoDocumento.NC).Sum(x => x.BaseMzn), g.Where(x => x.TipoDoc != TipoDocumento.NC).Sum(x => x.IvaMzn),
                g.Where(x => x.TipoDoc == TipoDocumento.NC).Sum(x => x.BaseMzn), g.Where(x => x.TipoDoc == TipoDocumento.NC).Sum(x => x.IvaMzn)))
            .OrderByDescending(l => l.TaxaPct).ThenBy(l => l.MotivoIsencao).ToList();
    }

    /// <summary>Documentos anulados no período (pela data de anulação).</summary>
    public async Task<List<LinhaAnulado>> AnuladosAsync(DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Relatorios.Fiscal)) return [];
        var inicio = de.ToDateTime(TimeOnly.MinValue).AddHours(-2);
        var fim = ate.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-2);
        return await db.Documentos.AsNoTracking()
            .Where(d => d.Estado == EstadoDocumento.Anulado && d.AnuladoEm >= inicio && d.AnuladoEm < fim)
            .OrderBy(d => d.AnuladoEm)
            .Select(d => new LinhaAnulado(d.Id, d.TipoDoc, d.NumeroFormatado ?? "", d.Data, d.ClienteNome, d.Total, d.AnuladoEm!.Value, d.AnuladoPor, d.MotivoAnulacao))
            .ToListAsync(ct);
    }
}
