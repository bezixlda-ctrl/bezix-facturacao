using System.Text;
using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Application.Importacao;

public sealed record LinhaCsv(int Numero, IReadOnlyDictionary<string, string> Valores)
{
    /// <summary>Valor da primeira coluna encontrada entre os nomes alternativos; null se vazia ou ausente.</summary>
    public string? Valor(params string[] nomes)
    {
        foreach (var n in nomes)
            if (Valores.TryGetValue(LeitorCsv.NormalizarCabecalho(n), out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
        return null;
    }
}

public sealed record FicheiroCsv(IReadOnlyList<string> Cabecalhos, IReadOnlyList<LinhaCsv> Linhas, char Separador);

/// <summary>
/// Leitor de CSV como o Excel em português o grava: separador ";" (ou "," / tab), UTF-8 com ou sem BOM
/// ou Windows-1252/Latin-1, campos entre aspas com aspas duplicadas e quebras de linha.
/// </summary>
public static class LeitorCsv
{
    public const int MaximoLinhas = 5000;

    public static FicheiroCsv Ler(byte[] conteudo)
    {
        var texto = Descodificar(conteudo);
        var primeiraLinha = texto.Split('\n', 2)[0];
        var separador = new[] { ';', ',', '\t' }.OrderByDescending(s => primeiraLinha.Count(c => c == s)).First();

        var registos = Separar(texto, separador);
        if (registos.Count == 0) return new FicheiroCsv([], [], separador);

        var cabecalhos = registos[0].Select(NormalizarCabecalho).ToList();
        var linhas = new List<LinhaCsv>();
        for (var i = 1; i < registos.Count; i++)
        {
            var campos = registos[i];
            if (campos.All(string.IsNullOrWhiteSpace)) continue;
            if (linhas.Count >= MaximoLinhas)
                throw new InvalidDataException($"O ficheiro tem mais de {MaximoLinhas} linhas. Divida-o em ficheiros mais pequenos.");

            var valores = new Dictionary<string, string>();
            for (var c = 0; c < cabecalhos.Count && c < campos.Count; c++)
                if (cabecalhos[c].Length > 0) valores.TryAdd(cabecalhos[c], campos[c]);
            linhas.Add(new LinhaCsv(i + 1, valores));
        }
        return new FicheiroCsv(cabecalhos, linhas, separador);
    }

    /// <summary>"Limite de crédito" → "limitedecredito"; "E-mail" → "email".</summary>
    public static string NormalizarCabecalho(string cabecalho) =>
        new(TextoPesquisa.Normalizar(cabecalho).Where(char.IsLetterOrDigit).ToArray());

    private static string Descodificar(byte[] bytes)
    {
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return utf8.GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            // Excel em Windows grava "CSV (separado por vírgulas)" em Windows-1252; Latin-1 cobre os acentos portugueses.
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static List<List<string>> Separar(string texto, char separador)
    {
        var registos = new List<List<string>>();
        var actual = new List<string>();
        var campo = new StringBuilder();
        var entreAspas = false;

        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entreAspas)
            {
                if (c == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                    else entreAspas = false;
                }
                else campo.Append(c);
                continue;
            }

            if (c == '"' && campo.Length == 0) entreAspas = true;
            else if (c == separador) { actual.Add(campo.ToString()); campo.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n')
            {
                actual.Add(campo.ToString()); campo.Clear();
                registos.Add(actual); actual = [];
            }
            else campo.Append(c);
        }
        if (campo.Length > 0 || actual.Count > 0)
        {
            actual.Add(campo.ToString());
            registos.Add(actual);
        }
        return registos;
    }
}
