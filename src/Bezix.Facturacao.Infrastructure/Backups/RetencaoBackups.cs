using System.Globalization;

namespace Bezix.Facturacao.Infrastructure.Backups;

/// <summary>
/// Retenção: o backup mais recente de cada um dos últimos 7 dias, de cada uma das últimas 4 semanas e de cada um dos
/// últimos 12 meses (contados entre os períodos que têm backups). O resto apaga-se do destino.
/// </summary>
public static class RetencaoBackups
{
    public const int Diarios = 7;
    public const int Semanais = 4;
    public const int Mensais = 12;

    /// <summary>Recebe a data de cada conjunto de backup e devolve as que se mantêm.</summary>
    public static IReadOnlySet<DateTime> AManter(IEnumerable<DateTime> conjuntos)
    {
        var lista = conjuntos.Distinct().OrderByDescending(d => d).ToList();
        var manter = new HashSet<DateTime>();
        void Mais(Func<DateTime, object> periodo, int quantos)
        {
            foreach (var g in lista.GroupBy(periodo).Take(quantos)) manter.Add(g.First());
        }
        Mais(d => d.Date, Diarios);
        Mais(d => (ISOWeek.GetYear(d), ISOWeek.GetWeekOfYear(d)), Semanais);
        Mais(d => (d.Year, d.Month), Mensais);
        return manter;
    }
}
