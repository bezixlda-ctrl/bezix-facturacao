using System.Globalization;
using System.Text;

namespace Bezix.Facturacao.Domain.Comum;

public static class TextoPesquisa
{
    /// <summary>Minúsculas, sem acentos nem espaços extra: "  Conceição " → "conceicao".</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var decomposto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return string.Join(' ', sb.ToString().Normalize(NormalizationForm.FormC).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Só os algarismos: "+258 84 123 4567" → "258841234567".</summary>
    public static string Algarismos(string? texto) =>
        new((texto ?? "").Where(char.IsAsciiDigit).ToArray());
}
