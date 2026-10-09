namespace Bezix.Facturacao.Domain.Comum;

/// <summary>Valor por extenso em português: 28.586,20 → "vinte e oito mil quinhentos e oitenta e seis meticais e vinte centavos".</summary>
public static class PorExtenso
{
    private static readonly string[] Unidades =
        ["zero", "um", "dois", "três", "quatro", "cinco", "seis", "sete", "oito", "nove", "dez",
         "onze", "doze", "treze", "catorze", "quinze", "dezasseis", "dezassete", "dezoito", "dezanove"];
    private static readonly string[] Dezenas = ["", "", "vinte", "trinta", "quarenta", "cinquenta", "sessenta", "setenta", "oitenta", "noventa"];
    private static readonly string[] Centenas = ["", "cento", "duzentos", "trezentos", "quatrocentos", "quinhentos", "seiscentos", "setecentos", "oitocentos", "novecentos"];

    public static string Valor(decimal valor, string singular, string plural, string centimoSingular, string centimoPlural)
    {
        if (valor < 0) return "menos " + Valor(-valor, singular, plural, centimoSingular, centimoPlural);
        valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
        var inteiro = (long)decimal.Truncate(valor);
        var centimos = (int)((valor - inteiro) * 100);

        var partes = new List<string>();
        if (inteiro > 0 || centimos == 0)
        {
            // "um milhão de meticais": de antes da moeda quando termina em milhão/milhões exactos.
            var de = inteiro >= 1_000_000 && inteiro % 1_000_000 == 0 ? " de" : "";
            partes.Add($"{Numero(inteiro)}{de} {(inteiro == 1 ? singular : plural)}");
        }
        if (centimos > 0) partes.Add($"{Numero(centimos)} {(centimos == 1 ? centimoSingular : centimoPlural)}");
        return string.Join(" e ", partes);
    }

    public static string Numero(long n)
    {
        if (n < 20) return Unidades[n];
        if (n < 100) return Dezenas[n / 10] + (n % 10 > 0 ? " e " + Unidades[n % 10] : "");
        if (n == 100) return "cem";
        if (n < 1000) return Centenas[n / 100] + (n % 100 > 0 ? " e " + Numero(n % 100) : "");

        var grupos = new (long Valor, string Singular, string Plural)[]
        {
            (1_000_000_000_000, "um bilião", "biliões"),
            (1_000_000_000, "mil milhões", "mil milhões"),
            (1_000_000, "um milhão", "milhões"),
            (1_000, "mil", "mil"),
        };

        var partes = new List<string>();
        long ultimoCoeficiente = 0;
        var resto = n;
        foreach (var (valor, singular, plural) in grupos)
        {
            if (resto < valor) continue;
            var q = resto / valor;
            resto %= valor;
            ultimoCoeficiente = q;
            if (valor == 1_000) partes.Add(q == 1 ? "mil" : $"{Numero(q)} mil");
            else if (valor == 1_000_000_000) partes.Add(q == 1 ? "mil milhões" : $"{Numero(q)} mil milhões");
            else partes.Add(q == 1 ? singular : $"{Numero(q)} {plural}");
        }
        if (resto > 0)
        {
            partes.Add(Numero(resto));
            ultimoCoeficiente = resto;
        }

        // Regra do "e": antes do último grupo quando este é menor que 100 ou centena exacta
        // ("dois mil e cinco", "mil e cem", "um milhão e quinhentos mil").
        if (partes.Count > 1 && (ultimoCoeficiente < 100 || ultimoCoeficiente % 100 == 0))
            return string.Join(" ", partes[..^1]) + " e " + partes[^1];
        return string.Join(" ", partes);
    }
}
