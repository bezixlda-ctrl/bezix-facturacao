using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Bezix.Facturacao.Domain.Documentos;

/// <summary>
/// Encadeamento de integridade: cada documento guarda o SHA-256 de (data; data/hora de emissão; número; total; hash anterior da série).
/// Alterar um documento antigo quebra todos os hashes seguintes da série, o que se detecta com <see cref="VerificarCadeia"/>.
/// A assinatura digital exigida pela AT (chave privada) será acrescentada quando a AT indicar o algoritmo (fase 6).
/// </summary>
public static class HashDocumento
{
    public static string Mensagem(DateOnly data, DateTime emitidoEmUtc, string numeroFormatado, decimal total, string? hashAnterior) =>
        string.Join(';',
            data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.SpecifyKind(emitidoEmUtc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            numeroFormatado,
            total.ToString("0.00", CultureInfo.InvariantCulture),
            hashAnterior ?? "");

    public static string Calcular(DateOnly data, DateTime emitidoEmUtc, string numeroFormatado, decimal total, string? hashAnterior) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Mensagem(data, emitidoEmUtc, numeroFormatado, total, hashAnterior))));

    public static string Calcular(Documento d) =>
        Calcular(d.Data, d.EmitidoEm ?? throw new InvalidOperationException("Documento sem data de emissão."),
            d.NumeroFormatado ?? throw new InvalidOperationException("Documento sem número."), d.Total, d.HashAnterior);

    /// <summary>4 caracteres impressos no rodapé (posições 1, 11, 21 e 31 do hash).</summary>
    public static string Curto(string? hash) =>
        hash is { Length: >= 31 } ? $"{hash[0]}{hash[10]}{hash[20]}{hash[30]}" : "";

    /// <summary>Verifica uma série ordenada por número. Devolve o primeiro número com problema, ou null se a cadeia está íntegra.</summary>
    public static int? VerificarCadeia(IEnumerable<Documento> serieOrdenada)
    {
        string? anterior = null;
        foreach (var d in serieOrdenada)
        {
            if (d.HashAnterior != anterior || d.Hash != Calcular(d)) return d.Numero;
            anterior = d.Hash;
        }
        return null;
    }
}
