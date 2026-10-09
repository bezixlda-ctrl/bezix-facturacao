using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Comunicacao;

/// <summary>
/// Link sem login para o cliente ver um documento. Guarda-se só o hash do token (quem tiver acesso à base
/// de dados não consegue abrir os links). Expira; regista a primeira e a última visita.
/// </summary>
public class LinkPartilha : Entidade
{
    public int DocumentoId { get; set; }
    public int ClienteId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiraEm { get; set; }
    public DateTime? PrimeiraVisita { get; set; }
    public DateTime? UltimaVisita { get; set; }
    public int Visitas { get; set; }
    public DateTime? RevogadoEm { get; set; }
}

/// <summary>Pagamento que o cliente diz ter feito (pela página do link), com comprovativo. Só vira recibo depois de validado.</summary>
public class PagamentoComunicado : Entidade
{
    public int ClienteId { get; set; }
    public int? DocumentoId { get; set; }
    public decimal Valor { get; set; }
    public DateOnly Data { get; set; }
    public string? Referencia { get; set; }
    public string? Mensagem { get; set; }
    public string? ComprovativoPath { get; set; }
    public string? ComprovativoTipo { get; set; }
    public EstadoPagamentoComunicado Estado { get; set; } = EstadoPagamentoComunicado.PorValidar;
    public string? DecididoPor { get; set; }
    public DateTime? DecididoEm { get; set; }
    public string? MotivoRejeicao { get; set; }
    public int? ReciboId { get; set; }
    public string? Ip { get; set; }
}

public enum EstadoPagamentoComunicado { PorValidar = 0, Validado = 1, Rejeitado = 2 }
