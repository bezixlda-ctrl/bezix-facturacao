using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Documentos;

/// <summary>
/// Documento comercial ou fiscal. Enquanto Rascunho é editável e não tem número.
/// Depois de emitido é imutável: só muda o estado para Anulado (com motivo) e o caminho do PDF.
/// Guarda cópia dos dados do cliente e do emitente no momento da emissão.
/// </summary>
public class Documento : Entidade
{
    public TipoDocumento TipoDoc { get; set; }
    public int SerieId { get; set; }
    public Serie? Serie { get; set; }

    /// <summary>Atribuído só na emissão.</summary>
    public int? Numero { get; set; }
    public string? NumeroFormatado { get; set; }

    public EstadoDocumento Estado { get; set; } = EstadoDocumento.Rascunho;

    public DateOnly Data { get; set; }
    public DateOnly? Vencimento { get; set; }

    // ---------- Cliente (cópia no momento da emissão) ----------
    public int ClienteId { get; set; }
    public string ClienteNome { get; set; } = "";
    public string? ClienteNuit { get; set; }
    public string? ClienteEndereco { get; set; }
    public string? ClienteCidade { get; set; }
    public string? ClienteTelefone { get; set; }
    public string? ClienteCelular { get; set; }
    public string? ClienteContacto { get; set; }
    public string? ClienteEmail { get; set; }

    /// <summary>Dados do emitente (nome, NUIT, endereço, contas, rodapé, logótipo) congelados na emissão, em JSON.</summary>
    public string? EmitenteJson { get; set; }

    // ---------- Moeda ----------
    public string Moeda { get; set; } = Configuracao.Moeda.Base;

    /// <summary>MZN por 1 unidade da moeda do documento (1 quando a moeda é MZN).</summary>
    public decimal Cambio { get; set; } = 1m;

    // ---------- Totais na moeda do documento ----------
    public decimal Subtotal { get; set; }
    public decimal DescontoGlobalPct { get; set; }
    public decimal DescontoGlobal { get; set; }
    public decimal BaseTributavel { get; set; }
    public decimal TotalIva { get; set; }
    public decimal Total { get; set; }

    // ---------- Contravalor em MZN (fiscal) ----------
    public decimal BaseTributavelMzn { get; set; }
    public decimal TotalIvaMzn { get; set; }
    public decimal TotalMzn { get; set; }

    public string? Observacoes { get; set; }

    // ---------- Pagamento no acto (FR) ----------
    public MetodoPagamento? MetodoPagamento { get; set; }
    public string? ReferenciaPagamento { get; set; }

    // ---------- Guia de remessa ----------
    public string? LocalCarga { get; set; }
    public string? LocalDescarga { get; set; }
    public string? Matricula { get; set; }

    // ---------- Correcções (NC/ND) ----------
    /// <summary>Documento corrigido por esta NC/ND.</summary>
    public int? DocOrigemId { get; set; }
    public Documento? DocOrigem { get; set; }
    public string? MotivoCorreccao { get; set; }

    // ---------- Emissão e anulação ----------
    public DateTime? EmitidoEm { get; set; }
    public string? EmitidoPor { get; set; }
    public string? Hash { get; set; }
    public string? HashAnterior { get; set; }
    public string? PdfPath { get; set; }

    public DateTime? AnuladoEm { get; set; }
    public string? AnuladoPor { get; set; }
    public string? MotivoAnulacao { get; set; }

    /// <summary>Utilizador responsável (criador). Usado na visibilidade "só os próprios".</summary>
    public string? ResponsavelId { get; set; }

    public List<DocumentoLinha> Linhas { get; set; } = [];
    public List<DocumentoIvaResumo> IvaResumo { get; set; } = [];

    /// <summary>Documentos que deram origem a este por conversão (CT → FT, GR → FT/FG).</summary>
    public List<DocumentoOrigem> Origens { get; set; } = [];

    public bool Emitido => Estado is EstadoDocumento.Emitido or EstadoDocumento.Anulado;
    public bool EmMoedaEstrangeira => Moeda != Configuracao.Moeda.Base;

    /// <summary>"FT_A2026-0008_Cliente.pdf"</summary>
    public string NomeFicheiroPdf()
    {
        var numero = (NumeroFormatado ?? $"{TipoDoc} rascunho {Id}").Replace(' ', '_').Replace('/', '-');
        var cliente = new string(ClienteNome.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim().Replace(' ', '_');
        if (cliente.Length > 40) cliente = cliente[..40];
        return $"{numero}_{cliente}.pdf";
    }
}

public class DocumentoLinha
{
    public long Id { get; set; }
    public int DocumentoId { get; set; }
    public int Ordem { get; set; }

    public int? ArtigoId { get; set; }
    public string? Ref { get; set; }
    public string Descricao { get; set; } = "";
    public string? Unidade { get; set; }

    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal DescontoPct { get; set; }

    public string CodigoTaxaIva { get; set; } = "";
    public decimal TaxaIvaPct { get; set; }
    public string? MotivoIsencao { get; set; }

    public decimal Base { get; set; }
    public decimal Iva { get; set; }
    public decimal TotalLinha { get; set; }
}

public class DocumentoIvaResumo
{
    public long Id { get; set; }
    public int DocumentoId { get; set; }
    public decimal TaxaPct { get; set; }
    public string? MotivoIsencao { get; set; }
    public decimal Base { get; set; }
    public decimal Iva { get; set; }
    public decimal BaseMzn { get; set; }
    public decimal IvaMzn { get; set; }
}

/// <summary>Ligação de conversão: este documento nasceu de outro (cotação ou guia).</summary>
public class DocumentoOrigem
{
    public int DocumentoId { get; set; }
    public int OrigemId { get; set; }
    public Documento? Origem { get; set; }
}

public enum EstadoDocumento
{
    Rascunho = 0,
    AguardaAprovacao = 1,
    Emitido = 2,
    Anulado = 3,
}

public enum MetodoPagamento
{
    Numerario = 0,
    Transferencia = 1,
    MPesa = 2,
    EMola = 3,
    Cheque = 4,
    Pos = 5,
}

public static class MetodoPagamentoInfo
{
    public static string Nome(MetodoPagamento m) => m switch
    {
        MetodoPagamento.Numerario => "Numerário",
        MetodoPagamento.Transferencia => "Transferência bancária",
        MetodoPagamento.MPesa => "M-Pesa",
        MetodoPagamento.EMola => "e-Mola",
        MetodoPagamento.Cheque => "Cheque",
        MetodoPagamento.Pos => "POS (cartão)",
        _ => m.ToString(),
    };
}
