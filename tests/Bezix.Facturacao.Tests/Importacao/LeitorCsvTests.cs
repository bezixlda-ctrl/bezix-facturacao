using System.Text;
using Bezix.Facturacao.Application.Importacao;

namespace Bezix.Facturacao.Tests.Importacao;

public class LeitorCsvTests
{
    [Fact]
    public void Le_CSV_do_Excel_em_portugues_com_ponto_e_virgula_e_acentos_em_Latin1()
    {
        var bytes = Encoding.Latin1.GetBytes("Nome;Endereço;Limite crédito\r\nConceição & Filhos;Av. Mao Tsé-Tung, 12;50.000,00\r\n");

        var f = LeitorCsv.Ler(bytes);

        Assert.Equal(';', f.Separador);
        Assert.Equal(["nome", "endereco", "limitecredito"], f.Cabecalhos);
        var l = Assert.Single(f.Linhas);
        Assert.Equal("Conceição & Filhos", l.Valor("nome"));
        Assert.Equal("Av. Mao Tsé-Tung, 12", l.Valor("Endereço"));
        Assert.Equal("50.000,00", l.Valor("Limite de crédito", "limitecredito"));
    }

    [Fact]
    public void Le_UTF8_com_BOM_virgulas_e_campos_entre_aspas()
    {
        var csv = "﻿nome,notas,cidade\n\"Silva, Lda\",\"Disse \"\"urgente\"\"\nsegunda linha\",Beira\n";

        var f = LeitorCsv.Ler(Encoding.UTF8.GetBytes(csv));

        Assert.Equal(',', f.Separador);
        var l = Assert.Single(f.Linhas);
        Assert.Equal("Silva, Lda", l.Valor("nome"));
        Assert.Equal("Disse \"urgente\"\nsegunda linha", l.Valor("notas"));
        Assert.Equal("Beira", l.Valor("cidade"));
    }

    [Fact]
    public void Ignora_linhas_vazias_e_numera_pela_linha_do_ficheiro()
    {
        var f = LeitorCsv.Ler(Encoding.UTF8.GetBytes("nome;nuit\n\nA;1\n;\nB;2\n"));

        Assert.Equal([3, 5], f.Linhas.Select(l => l.Numero));
    }

    [Fact]
    public void Coluna_vazia_ou_inexistente_devolve_null()
    {
        var l = LeitorCsv.Ler(Encoding.UTF8.GetBytes("nome;email\nA;  \n")).Linhas[0];

        Assert.Null(l.Valor("email"));
        Assert.Null(l.Valor("telefone"));
    }

    [Fact]
    public void Recusa_ficheiros_demasiado_grandes()
    {
        var sb = new StringBuilder("nome\n");
        for (var i = 0; i <= LeitorCsv.MaximoLinhas; i++) sb.Append("x").Append(i).Append('\n');

        Assert.Throws<InvalidDataException>(() => LeitorCsv.Ler(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
