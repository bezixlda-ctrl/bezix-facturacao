using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Documentos;

/// <summary>Pedido de aprovação: desconto acima do limite do perfil ou nota de crédito pedida por quem não a pode emitir.</summary>
public class Aprovacao : Entidade
{
    public int DocumentoId { get; set; }
    public Documento? Documento { get; set; }
    public TipoAprovacao Tipo { get; set; }
    public string PedidoPorId { get; set; } = "";
    public string PedidoPorNome { get; set; } = "";
    public string? Justificacao { get; set; }
    public EstadoAprovacao Estado { get; set; } = EstadoAprovacao.Pendente;
    public string? DecididoPorNome { get; set; }
    public DateTime? DecididoEm { get; set; }
    public string? NotaDecisao { get; set; }
}

public enum TipoAprovacao { Desconto = 0, NotaCredito = 1 }

public enum EstadoAprovacao { Pendente = 0, Aprovada = 1, Rejeitada = 2 }
