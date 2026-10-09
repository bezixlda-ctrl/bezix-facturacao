using Bezix.Facturacao.Domain.Comum;
using Bezix.Facturacao.Domain.Documentos;

namespace Bezix.Facturacao.Tests.Domain;

public class DocumentoDominioTests
{
    [Theory]
    [InlineData(28_586.20, "vinte e oito mil quinhentos e oitenta e seis meticais e vinte centavos")]
    [InlineData(1, "um metical")]
    [InlineData(0.5, "cinquenta centavos")]
    [InlineData(100, "cem meticais")]
    [InlineData(101, "cento e um meticais")]
    [InlineData(2_005, "dois mil e cinco meticais")]
    [InlineData(1_100, "mil e cem meticais")]
    [InlineData(1_000_000, "um milhão de meticais")]
    [InlineData(1_500_000, "um milhão e quinhentos mil meticais")]
    [InlineData(2_345_678.91, "dois milhões trezentos e quarenta e cinco mil seiscentos e setenta e oito meticais e noventa e um centavos")]
    public void Valor_por_extenso(decimal valor, string esperado) =>
        Assert.Equal(esperado, PorExtenso.Valor(valor, "metical", "meticais", "centavo", "centavos"));

    [Fact]
    public void Hash_encadeado_detecta_alteracao_de_documento_antigo()
    {
        var docs = new List<Documento>();
        string? anterior = null;
        for (var i = 1; i <= 3; i++)
        {
            var d = new Documento
            {
                Numero = i, NumeroFormatado = $"FT A2026/{i:0000}", Data = new DateOnly(2026, 10, i),
                EmitidoEm = new DateTime(2026, 10, i, 9, 0, 0, DateTimeKind.Utc), Total = 100 * i, HashAnterior = anterior,
            };
            d.Hash = HashDocumento.Calcular(d);
            anterior = d.Hash;
            docs.Add(d);
        }
        Assert.Null(HashDocumento.VerificarCadeia(docs));
        Assert.Equal(64, docs[0].Hash!.Length);
        Assert.Equal(4, HashDocumento.Curto(docs[0].Hash).Length);

        docs[1].Total = 1; // alguém mexeu na base de dados
        Assert.Equal(2, HashDocumento.VerificarCadeia(docs));
    }

    [Fact]
    public void Nome_do_ficheiro_PDF_segue_o_formato_da_especificacao()
    {
        var d = new Documento { TipoDoc = TipoDocumento.FT, NumeroFormatado = "FT A2026/0008", ClienteNome = "Conceição & Filhos, Lda" };
        Assert.Equal("FT_A2026-0008_Conceição__Filhos_Lda.pdf", d.NomeFicheiroPdf());
    }
}
