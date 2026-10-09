using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Configuracao;

/// <summary>Moeda em que um documento pode ser emitido. A moeda base (contabilística e fiscal) é sempre o MZN.</summary>
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

/// <summary>Câmbio de referência (ex.: Banco de Moçambique): quantos MZN vale 1 unidade da moeda, a partir de uma data.</summary>
public class TaxaCambio : Entidade
{
    public string Moeda { get; set; } = "";
    public DateOnly Data { get; set; }

    /// <summary>MZN por 1 unidade da moeda (6 casas decimais).</summary>
    public decimal Valor { get; set; }
}
