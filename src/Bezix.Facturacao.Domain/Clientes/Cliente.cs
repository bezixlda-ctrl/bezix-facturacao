using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Clientes;

public class Cliente : Entidade
{
    public string Nome { get; set; } = "";
    public TipoCliente Tipo { get; set; } = TipoCliente.Empresa;

    /// <summary>Obrigatório para empresas; para particulares só acima do limite configurado (verificado na emissão).</summary>
    public string? Nuit { get; set; }

    public string? Endereco { get; set; }
    public string? Cidade { get; set; }
    public string? Telefone { get; set; }
    public string? Celular { get; set; }

    /// <summary>Só os algarismos do telefone e do celular, para pesquisar "841234567" em "+258 84 123 4567".</summary>
    public string? TelefonesPesquisa { get; set; }

    public string? Email { get; set; }

    /// <summary>Pessoa de contacto, impressa como "Att." no documento.</summary>
    public string? Contacto { get; set; }

    /// <summary>Prazo de pagamento em dias; nulo = prazo por omissão da empresa.</summary>
    public int? PrazoPagamentoDias { get; set; }

    public decimal? LimiteCredito { get; set; }
    public decimal DescontoPadraoPct { get; set; }
    public string? Notas { get; set; }

    /// <summary>Etiquetas separadas por vírgula (ex.: "governo, revenda").</summary>
    public string? Etiquetas { get; set; }

    public bool Activo { get; set; } = true;

    /// <summary>Utilizador responsável (Id do Identity). Usado na visibilidade "só os próprios".</summary>
    public string? ResponsavelId { get; set; }

    // Excepções de comunicação (usadas na fase 5)
    public string? EmailFacturacao { get; set; }
    public string? EmailCc { get; set; }
    public bool LembretesActivos { get; set; } = true;

    public List<Actividade> Actividades { get; set; } = [];

    public IEnumerable<string> ListaEtiquetas() => NormalizarEtiquetas(Etiquetas);

    public static IEnumerable<string> NormalizarEtiquetas(string? etiquetas) =>
        (etiquetas ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .Distinct();

    /// <summary>Email para onde vão facturas e lembretes.</summary>
    public string? EmailEnvio => string.IsNullOrWhiteSpace(EmailFacturacao) ? Email : EmailFacturacao;
}

public enum TipoCliente
{
    Empresa = 0,
    Particular = 1,
}
