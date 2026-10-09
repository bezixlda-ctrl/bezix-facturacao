using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Configuracao;

/// <summary>Registo único com os dados fiscais do emitente (empresa única, sem TenantId).</summary>
public class Empresa : Entidade
{
    public const int IdUnico = 1;

    public string Nome { get; set; } = "";
    public string Nuit { get; set; } = "";
    public string? Slogan { get; set; }
    public string Endereco { get; set; } = "";
    public string? Cidade { get; set; }
    public string? Provincia { get; set; }
    public string? Telefones { get; set; }
    public string? Email { get; set; }
    public string? Site { get; set; }

    /// <summary>Caminho relativo dentro de App_Data/uploads.</summary>
    public string? LogoPath { get; set; }

    /// <summary>Cor da marca em hexadecimal (#RRGGBB), usada como cor primária da interface e do PDF.</summary>
    public string CorPrimaria { get; set; } = "#0F5C8C";

    public string RodapeTexto { get; set; } = "Processado por computador";
    public string Moeda { get; set; } = "MZN";

    /// <summary>Prazo de pagamento por omissão, em dias.</summary>
    public int PrazoPagamentoDias { get; set; } = 30;

    /// <summary>Valor a partir do qual o NUIT do cliente particular é obrigatório (confirmar com a AT).</summary>
    public decimal LimiteNuitObrigatorio { get; set; }

    public ModoArredondamentoIva ModoArredondamentoIva { get; set; } = ModoArredondamentoIva.PorTaxa;

    /// <summary>
    /// Quando activo, utilizadores sem a permissão Documentos.VerTodos (ex.: Facturador) só vêem
    /// os clientes e documentos de que são responsáveis.
    /// </summary>
    public bool RestringirAosProprios { get; set; }
}

public enum ModoArredondamentoIva
{
    /// <summary>IVA calculado por taxa sobre a soma das bases (regra por omissão).</summary>
    PorTaxa = 0,
    /// <summary>IVA arredondado linha a linha e somado.</summary>
    PorLinha = 1,
}
