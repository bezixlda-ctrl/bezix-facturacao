using Bezix.Facturacao.Web.Formatacao;

namespace Bezix.Facturacao.Tests.Web;

public class FormatoTests
{
    [Fact]
    public void Moeda_no_formato_mocambicano() => Assert.Equal("30.804,10 MT", Formato.Moeda(30_804.10m));

    [Fact]
    public void Datas_em_UTC_mostradas_na_hora_de_Maputo()
    {
        // 22:30 UTC de 31/12 = 00:30 de 01/01 em Maputo (UTC+2)
        var utc = new DateTime(2025, 12, 31, 22, 30, 0, DateTimeKind.Utc);
        Assert.Equal("01/01/2026 00:30", Formato.DataHora(utc));
    }

    [Theory]
    [InlineData("30.804,10", 30804.10)]
    [InlineData("30804,10", 30804.10)]
    [InlineData("30804.10", 30804.10)]
    [InlineData("1.000", 1000)]
    [InlineData("16", 16)]
    [InlineData("20%", 20)]
    [InlineData("1 500,00 MT", 1500)]
    public void Le_valores_escritos_de_varias_formas(string texto, decimal esperado)
    {
        Assert.True(Formato.TentarLerDecimal(texto, out var valor));
        Assert.Equal(esperado, valor);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    public void Rejeita_texto_que_nao_e_numero(string texto) => Assert.False(Formato.TentarLerDecimal(texto, out _));
}
