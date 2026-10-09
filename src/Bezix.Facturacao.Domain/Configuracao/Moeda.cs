using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Configuracao;

/// <summary>
/// Moeda dos documentos. Todos os documentos são emitidos em meticais (MZN), como exige a legislação fiscal
/// moçambicana; a tabela guarda os nomes usados no valor por extenso.
/// </summary>
public class Moeda : Entidade
{
    public const string Base = "MZN";

    /// <summary>Código ISO 4217 (MZN, USD, EUR, ZAR).</summary>
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Simbolo { get; set; } = "";

    // Para o valor por extenso: "um metical", "dois meticais", "cinquenta centavos".
    public string NomeSingular { get; set; } = "";
    public string NomePlural { get; set; } = "";
    public string CentimoSingular { get; set; } = "centavo";
    public string CentimoPlural { get; set; } = "centavos";

    public bool Activa { get; set; } = true;
    public int Ordem { get; set; }

    public bool IsBase => Codigo == Base;
}
