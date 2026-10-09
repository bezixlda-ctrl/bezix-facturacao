using System.Globalization;

namespace Bezix.Facturacao.Web.Formatacao;

/// <summary>Formato Moçambique: 30.804,10 MT, datas dd/mm/aaaa, horas em Africa/Maputo (as datas são guardadas em UTC).</summary>
public static class Formato
{
    public static readonly CultureInfo Cultura = CriarCultura();
    public static readonly TimeZoneInfo FusoMaputo = ObterFuso();

    private static CultureInfo CriarCultura()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("pt-PT").Clone();
        var n = c.NumberFormat;
        n.NumberGroupSeparator = n.CurrencyGroupSeparator = n.PercentGroupSeparator = ".";
        n.NumberDecimalSeparator = n.CurrencyDecimalSeparator = n.PercentDecimalSeparator = ",";
        n.CurrencySymbol = "MT";
        n.CurrencyPositivePattern = 3; // "n MT"
        n.CurrencyNegativePattern = 8; // "-n MT"
        c.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        c.DateTimeFormat.DateSeparator = "/";
        c.DateTimeFormat.ShortTimePattern = "HH:mm";
        return CultureInfo.ReadOnly(c);
    }

    private static TimeZoneInfo ObterFuso()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time"); }
    }

    public static string Moeda(decimal valor) => valor.ToString("#,##0.00", Cultura) + " MT";
    public static string Numero(decimal valor) => valor.ToString("#,##0.00", Cultura);
    public static string Percentagem(decimal valor) => valor.ToString("0.##", Cultura) + "%";

    public static DateTime ParaMaputo(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), FusoMaputo);

    public static string Data(DateTime utc) => ParaMaputo(utc).ToString("dd/MM/yyyy", Cultura);
    public static string Data(DateOnly data) => data.ToString("dd/MM/yyyy", Cultura);
    public static string DataHora(DateTime utc) => ParaMaputo(utc).ToString("dd/MM/yyyy HH:mm", Cultura);

    public static DateOnly HojeEmMaputo(TimeProvider relogio) =>
        DateOnly.FromDateTime(ParaMaputo(relogio.GetUtcNow().UtcDateTime));

    /// <summary>Aceita "30.804,10", "30804,10" e "30804.10" (este último só quando não há vírgula).</summary>
    public static bool TentarLerDecimal(string? texto, out decimal valor) =>
        Domain.Comum.Numeros.TentarLerDecimal(texto, out valor);
}
