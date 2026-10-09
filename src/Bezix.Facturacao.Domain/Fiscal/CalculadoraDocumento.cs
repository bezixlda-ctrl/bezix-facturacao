using Bezix.Facturacao.Domain.Configuracao;

namespace Bezix.Facturacao.Domain.Fiscal;

public sealed record LinhaCalculo(
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal DescontoPct,
    decimal TaxaIvaPct);

/// <param name="Base">Total líquido da linha (após desconto de linha, antes do desconto global e do IVA).</param>
/// <param name="Iva">IVA da linha; no modo PorTaxa é informativo (o valor fiscal é o do resumo por taxa).</param>
public sealed record LinhaCalculada(
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal DescontoPct,
    decimal TaxaIvaPct,
    decimal Base,
    decimal Iva,
    decimal TotalLinha);

public sealed record ResumoIva(decimal TaxaPct, decimal Base, decimal Iva);

public sealed record ResultadoCalculo(
    IReadOnlyList<LinhaCalculada> Linhas,
    decimal Subtotal,
    decimal DescontoGlobalPct,
    decimal DescontoGlobal,
    decimal BaseTributavel,
    IReadOnlyList<ResumoIva> IvaPorTaxa,
    decimal TotalIva,
    decimal Total);

/// <summary>
/// Cálculo de totais de um documento fiscal. Regras (ver docs/regras-calculo.md):
/// 1. Total líquido de cada linha = Qtd × Preço × (1 − Desc%), arredondado a 2 casas.
/// 2. Subtotal = soma das linhas.
/// 3. Desconto global = Subtotal × Desc.global%, arredondado a 2 casas; é sempre aplicado ANTES do IVA.
/// 4. O desconto global é repartido pelas taxas de IVA na proporção das bases; o resto do
///    arredondamento fica na taxa com maior base, para que a soma das bases bata com a base tributável.
/// 5. IVA (modo PorTaxa, por omissão) = Base da taxa × Taxa%, arredondado a 2 casas.
///    No modo PorLinha, o IVA de cada linha é arredondado e somado por taxa.
/// 6. Total = Base tributável + IVA.
/// Arredondamento comercial: meio para cima (MidpointRounding.AwayFromZero).
/// </summary>
public static class CalculadoraDocumento
{
    public static decimal Arredondar(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    public static ResultadoCalculo Calcular(
        IReadOnlyList<LinhaCalculo> linhas,
        decimal descontoGlobalPct,
        ModoArredondamentoIva modo = ModoArredondamentoIva.PorTaxa)
    {
        ArgumentNullException.ThrowIfNull(linhas);
        ValidarPercentagem(descontoGlobalPct, "desconto global");

        var factorGlobal = 1m - descontoGlobalPct / 100m;

        var calculadas = new List<LinhaCalculada>(linhas.Count);
        foreach (var l in linhas)
        {
            if (l.Quantidade <= 0) throw new ArgumentException("A quantidade tem de ser maior que zero.");
            if (l.PrecoUnitario < 0) throw new ArgumentException("O preço unitário não pode ser negativo.");
            ValidarPercentagem(l.DescontoPct, "desconto da linha");
            ValidarPercentagem(l.TaxaIvaPct, "taxa de IVA");

            var baseLinha = Arredondar(l.Quantidade * l.PrecoUnitario * (1m - l.DescontoPct / 100m));
            var ivaLinha = Arredondar(baseLinha * factorGlobal * l.TaxaIvaPct / 100m);
            calculadas.Add(new LinhaCalculada(l.Quantidade, l.PrecoUnitario, l.DescontoPct, l.TaxaIvaPct,
                baseLinha, ivaLinha, baseLinha));
        }

        var subtotal = calculadas.Sum(l => l.Base);
        var descontoGlobal = Arredondar(subtotal * descontoGlobalPct / 100m);
        var baseTributavel = subtotal - descontoGlobal;

        var grupos = calculadas
            .GroupBy(l => l.TaxaIvaPct)
            .Select(g => new { Taxa = g.Key, Bruto = g.Sum(x => x.Base), IvaLinhas = g.Sum(x => x.Iva) })
            .OrderByDescending(g => g.Bruto)
            .ThenByDescending(g => g.Taxa)
            .ToList();

        // Repartir o desconto global pelas taxas; a primeira (maior base) absorve o resto do arredondamento.
        var resumo = new List<ResumoIva>(grupos.Count);
        var descontoPorRepartir = descontoGlobal;
        for (var i = grupos.Count - 1; i >= 0; i--)
        {
            var g = grupos[i];
            var descontoTaxa = i == 0
                ? descontoPorRepartir
                : subtotal == 0 ? 0 : Arredondar(descontoGlobal * g.Bruto / subtotal);
            descontoPorRepartir -= descontoTaxa;

            var baseTaxa = g.Bruto - descontoTaxa;
            var iva = modo == ModoArredondamentoIva.PorTaxa
                ? Arredondar(baseTaxa * g.Taxa / 100m)
                : g.IvaLinhas;
            resumo.Add(new ResumoIva(g.Taxa, baseTaxa, iva));
        }

        resumo.Sort((a, b) => b.TaxaPct.CompareTo(a.TaxaPct));
        var totalIva = resumo.Sum(r => r.Iva);

        return new ResultadoCalculo(
            calculadas,
            subtotal,
            descontoGlobalPct,
            descontoGlobal,
            baseTributavel,
            resumo,
            totalIva,
            baseTributavel + totalIva);
    }

    private static void ValidarPercentagem(decimal valor, string nome)
    {
        if (valor is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(valor), $"A percentagem de {nome} tem de estar entre 0 e 100.");
    }
}
