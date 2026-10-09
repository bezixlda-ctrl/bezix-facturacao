using Bezix.Facturacao.Application.Automacao;
using Bezix.Facturacao.Domain.Automacao;

namespace Bezix.Facturacao.Tests.Automacao;

/// <summary>Calendário dos lembretes: 3 dias antes, e 1, 15 e 30 dias depois do vencimento, cada um uma única vez.</summary>
public class LembretesTests
{
    private static readonly DateOnly Vencimento = new(2026, 10, 15);
    private static readonly int[] Depois = [1, 15, 30];

    private static TipoLembrete? Devido(int diasDesdeVencimento, params TipoLembrete[] enviados) =>
        ServicoLembretes.Devido(Vencimento, Vencimento.AddDays(diasDesdeVencimento), 3, Depois, enviados.ToHashSet());

    [Theory]
    [InlineData(-10)]
    [InlineData(-4)]
    public void Cedo_demais_nao_envia_nada(int dias) => Assert.Null(Devido(dias));

    [Theory]
    [InlineData(-3)]
    [InlineData(-1)]
    public void Nos_ultimos_3_dias_antes_envia_o_aviso_previo_uma_vez(int dias)
    {
        Assert.Equal(TipoLembrete.Antes, Devido(dias));
        Assert.Null(Devido(dias, TipoLembrete.Antes));
    }

    [Fact]
    public void No_dia_do_vencimento_ainda_nao_e_atraso() => Assert.Null(Devido(0, TipoLembrete.Antes));

    [Fact]
    public void Avisos_de_atraso_saem_por_ordem_e_cada_um_uma_vez()
    {
        Assert.Equal(TipoLembrete.Aviso1, Devido(1, TipoLembrete.Antes));
        Assert.Null(Devido(2, TipoLembrete.Aviso1));
        Assert.Equal(TipoLembrete.Aviso2, Devido(15, TipoLembrete.Aviso1));
        Assert.Equal(TipoLembrete.Aviso3, Devido(30, TipoLembrete.Aviso1, TipoLembrete.Aviso2));
        Assert.Null(Devido(90, TipoLembrete.Aviso1, TipoLembrete.Aviso2, TipoLembrete.Aviso3));
    }

    [Fact]
    public void Se_o_sistema_esteve_parado_envia_so_o_aviso_seguinte_e_nao_todos_de_uma_vez()
    {
        // 40 dias de atraso sem nenhum aviso: sai o 1.º; o 2.º só no tick seguinte.
        Assert.Equal(TipoLembrete.Aviso1, Devido(40));
        Assert.Equal(TipoLembrete.Aviso2, Devido(40, TipoLembrete.Aviso1));
    }

    [Fact]
    public void Lembretes_manuais_nao_contam_como_avisos_automaticos() =>
        Assert.Equal(TipoLembrete.Aviso1, Devido(5, TipoLembrete.Manual));

    [Fact]
    public void Plano_le_os_dias_depois_por_ordem_e_respeita_o_maximo()
    {
        var p = new PlanoLembretes { DiasDepois = "30, 1,15,abc,15", MaxAvisos = 2 };
        Assert.Equal([1, 15], p.ListaDiasDepois());
    }

    [Fact]
    public void Recorrencia_mensal_no_fim_do_mes_usa_o_ultimo_dia_do_mes_seguinte()
    {
        var r = new Recorrencia { Periodicidade = Periodicidade.Mensal };
        Assert.Equal(new DateOnly(2026, 2, 28), r.Seguinte(new DateOnly(2026, 1, 31)));
        Assert.Equal(new DateOnly(2027, 1, 15), new Recorrencia { Periodicidade = Periodicidade.Anual }.Seguinte(new DateOnly(2026, 1, 15)));
    }
}
