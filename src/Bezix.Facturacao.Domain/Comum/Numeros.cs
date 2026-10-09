using System.Globalization;

namespace Bezix.Facturacao.Domain.Comum;

public static class Numeros
{
    private static readonly NumberFormatInfo FormatoMz = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
    };

    /// <summary>
    /// Lê valores escritos à moçambicana ou vindos do Excel: "30.804,10", "30804,10", "30804.10", "1 500,00 MT", "20%".
    /// Um único ponto seguido de 1, 2 ou 4+ algarismos é tratado como separador decimal; seguido de 3, como milhares.
    /// </summary>
    public static bool TentarLerDecimal(string? texto, out decimal valor)
    {
        valor = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var t = texto.Trim()
            .Replace(" ", "").Replace(" ", "")
            .Replace("MT", "", StringComparison.OrdinalIgnoreCase)
            .Replace("MZN", "", StringComparison.OrdinalIgnoreCase)
            .Replace("%", "");
        if (!t.Contains(',') && t.Count(ch => ch == '.') == 1 && t.Split('.')[1].Length != 3)
            t = t.Replace('.', ',');
        return decimal.TryParse(t, NumberStyles.Number, FormatoMz, out valor);
    }
}
