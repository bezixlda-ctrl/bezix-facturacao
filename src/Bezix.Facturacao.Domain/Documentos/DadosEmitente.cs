using System.Text.Json;
using Bezix.Facturacao.Domain.Configuracao;

namespace Bezix.Facturacao.Domain.Documentos;

public sealed record ContaEmitente(string Banco, string NumeroConta, string? Nib, string? Iban, string? Swift);

/// <summary>Cópia dos dados do emitente no momento da emissão (o PDF de um documento antigo usa sempre esta cópia).</summary>
public sealed record DadosEmitente(
    string Nome,
    string Nuit,
    string? Slogan,
    string Endereco,
    string? Cidade,
    string? Provincia,
    string? Telefones,
    string? Email,
    string? Site,
    string? LogoPath,
    string CorPrimaria,
    string RodapeTexto,
    IReadOnlyList<ContaEmitente> Contas,
    string? CarimboPath = null,
    string? AssinaturaPath = null,
    string? SignatarioNome = null,
    string? SignatarioCargo = null)
{
    /// <summary>Há carimbo ou assinatura para imprimir (congelados na emissão, como o logótipo).</summary>
    public bool TemCarimboOuAssinatura => CarimboPath is not null || AssinaturaPath is not null;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static DadosEmitente De(Empresa e, IEnumerable<ContaBancaria> contas) => new(
        e.Nome, e.Nuit, e.Slogan, e.Endereco, e.Cidade, e.Provincia, e.Telefones, e.Email, e.Site, e.LogoPath,
        e.CorPrimaria, e.RodapeTexto,
        contas.Where(c => c.MostrarNoDocumento).OrderBy(c => c.Ordem).ThenBy(c => c.Banco)
            .Select(c => new ContaEmitente(c.Banco, c.NumeroConta, c.Nib, c.Iban, c.Swift)).ToList(),
        e.CarimboPath, e.AssinaturaPath, e.SignatarioNome, e.SignatarioCargo);

    public string ParaJson() => JsonSerializer.Serialize(this, Json);

    public static DadosEmitente? DeJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<DadosEmitente>(json, Json);
}
