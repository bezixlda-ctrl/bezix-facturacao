using Bezix.Facturacao.Domain.Comum;
using Bezix.Facturacao.Domain.Documentos;

namespace Bezix.Facturacao.Domain.Recebimentos;

/// <summary>
/// Dinheiro recebido de um cliente, sempre em meticais. Cada pagamento tem o seu recibo (RC) e é alocado
/// a uma ou várias facturas/notas de débito. A parte não alocada fica como adiantamento (crédito do cliente).
/// </summary>
public class Pagamento : Entidade
{
    public int ClienteId { get; set; }
    public DateOnly Data { get; set; }
    public decimal Valor { get; set; }
    public MetodoPagamento Metodo { get; set; }
    public string? Referencia { get; set; }
    public string? Observacoes { get; set; }

    /// <summary>Recibo (documento RC) que dá quitação deste pagamento.</summary>
    public int ReciboId { get; set; }
    public Documento? Recibo { get; set; }

    public List<PagamentoAlocacao> Alocacoes { get; set; } = [];
}

/// <summary>Parte de um pagamento aplicada a um documento (FT, FG ou ND), em meticais.</summary>
public class PagamentoAlocacao : IRastreavel
{
    public long Id { get; set; }
    public int PagamentoId { get; set; }
    public Pagamento? Pagamento { get; set; }
    public int DocumentoId { get; set; }
    public Documento? Documento { get; set; }
    public decimal Valor { get; set; }

    /// <summary>Data em que a alocação foi feita (a do recibo, ou mais tarde quando se usa um adiantamento).</summary>
    public DateOnly Data { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Situação de pagamento de um documento a débito, derivada dos recibos e notas de crédito.</summary>
public enum EstadoPagamento
{
    /// <summary>Não se aplica (rascunho, anulado, cotação, guia, recibo, nota de crédito).</summary>
    NaoAplicavel = 0,
    PorPagar = 1,
    Parcial = 2,
    Pago = 3,
    Vencido = 4,
}
