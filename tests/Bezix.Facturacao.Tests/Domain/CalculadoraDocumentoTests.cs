using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Fiscal;

namespace Bezix.Facturacao.Tests.Domain;

public class CalculadoraDocumentoTests
{
    [Fact]
    public void Factura_modelo_08_26_aplica_desconto_antes_do_IVA_a_16_porcento()
    {
        // Critério de aceitação: 30.804,10 − 20% → base 24.643,28 → IVA 16% 3.942,92 → total 28.586,20
        var r = CalculadoraDocumento.Calcular([new LinhaCalculo(1, 30_804.10m, 0, 16)], descontoGlobalPct: 20);

        Assert.Equal(30_804.10m, r.Subtotal);
        Assert.Equal(6_160.82m, r.DescontoGlobal);
        Assert.Equal(24_643.28m, r.BaseTributavel);
        Assert.Equal(3_942.92m, r.TotalIva);
        Assert.Equal(28_586.20m, r.Total);
    }

    [Fact]
    public void Factura_modelo_nunca_produz_o_IVA_de_20_porcento_impresso_no_original()
    {
        // A factura modelo mostra IVA 4.928,66 (= 20% da base). O sistema calcula sempre pela taxa configurada.
        var r = CalculadoraDocumento.Calcular([new LinhaCalculo(1, 30_804.10m, 0, 16)], descontoGlobalPct: 20);

        Assert.NotEqual(4_928.66m, r.TotalIva);
    }

    [Fact]
    public void Arredonda_cada_linha_a_duas_casas()
    {
        var r = CalculadoraDocumento.Calcular([new LinhaCalculo(3, 0.335m, 0, 16)], 0);

        Assert.Equal(1.01m, r.Linhas[0].Base); // 1,005 → 1,01 (meio para cima)
    }

    [Fact]
    public void Desconto_de_linha_e_global_acumulam_antes_do_IVA()
    {
        var r = CalculadoraDocumento.Calcular([new LinhaCalculo(2, 1_000m, 10, 16)], descontoGlobalPct: 5);

        Assert.Equal(1_800m, r.Subtotal);
        Assert.Equal(90m, r.DescontoGlobal);
        Assert.Equal(1_710m, r.BaseTributavel);
        Assert.Equal(273.60m, r.TotalIva);
        Assert.Equal(1_983.60m, r.Total);
    }

    [Fact]
    public void Varias_taxas_repartem_o_desconto_global_e_somam_a_base_tributavel()
    {
        var r = CalculadoraDocumento.Calcular(
        [
            new LinhaCalculo(1, 333.33m, 0, 16),
            new LinhaCalculo(1, 666.67m, 0, 0),
            new LinhaCalculo(1, 100.01m, 0, 16),
        ], descontoGlobalPct: 7);

        Assert.Equal(r.BaseTributavel, r.IvaPorTaxa.Sum(t => t.Base));
        Assert.Equal(2, r.IvaPorTaxa.Count);
        var isento = r.IvaPorTaxa.Single(t => t.TaxaPct == 0);
        Assert.Equal(0m, isento.Iva);
        Assert.Equal(r.BaseTributavel + r.TotalIva, r.Total);
    }

    [Fact]
    public void Modo_por_linha_soma_o_IVA_arredondado_de_cada_linha()
    {
        LinhaCalculo[] linhas = [new(1, 0.03m, 0, 16), new(1, 0.03m, 0, 16), new(1, 0.03m, 0, 16)];

        var porTaxa = CalculadoraDocumento.Calcular(linhas, 0, ModoArredondamentoIva.PorTaxa);
        var porLinha = CalculadoraDocumento.Calcular(linhas, 0, ModoArredondamentoIva.PorLinha);

        Assert.Equal(0.01m, porTaxa.TotalIva);  // 0,09 × 16% = 0,0144 → 0,01
        Assert.Equal(0.00m, porLinha.TotalIva); // 3 × (0,03 × 16% = 0,0048 → 0,00)
    }

    [Theory]
    [InlineData(0, 10, 0, 16)]
    [InlineData(1, -1, 0, 16)]
    [InlineData(1, 10, 101, 16)]
    [InlineData(1, 10, 0, -1)]
    public void Rejeita_valores_invalidos(decimal qtd, decimal preco, decimal desconto, decimal taxa)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            CalculadoraDocumento.Calcular([new LinhaCalculo(qtd, preco, desconto, taxa)], 0));
    }

    [Fact]
    public void Documento_sem_linhas_tem_total_zero()
    {
        var r = CalculadoraDocumento.Calcular([], 10);
        Assert.Equal(0m, r.Total);
        Assert.Empty(r.IvaPorTaxa);
    }
}
