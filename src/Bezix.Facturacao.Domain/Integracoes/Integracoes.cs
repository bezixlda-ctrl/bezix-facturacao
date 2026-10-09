using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Integracoes;

/// <summary>Chave da API v1. Só se guarda o hash; a chave completa mostra-se uma única vez, ao criar.</summary>
public class ChaveApi : Entidade
{
    public string Nome { get; set; } = "";

    /// <summary>Parte pública da chave (identifica-a nos logs e na lista sem a revelar).</summary>
    public string Prefixo { get; set; } = "";
    public string HashChave { get; set; } = "";

    /// <summary>Escopos separados por espaço (ex.: "clientes:ler documentos:emitir").</summary>
    public string Escopos { get; set; } = "";

    public DateTime? UltimoUso { get; set; }
    public DateTime? RevogadaEm { get; set; }
    public bool Activa => RevogadaEm is null;

    public IReadOnlyList<string> ListaEscopos() => Escopos.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Resposta guardada de um POST com Idempotency-Key: repetir o pedido devolve a mesma resposta sem duplicar.</summary>
public class PedidoIdempotente
{
    public long Id { get; set; }
    public int ChaveApiId { get; set; }
    public string Chave { get; set; } = "";
    public string Rota { get; set; } = "";
    public string HashCorpo { get; set; } = "";
    public int? CodigoResposta { get; set; }
    public string? Resposta { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class WebhookSubscricao : Entidade
{
    public string Nome { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>Segredo para a assinatura HMAC, cifrado com a Data Protection do ASP.NET.</summary>
    public string SegredoCifrado { get; set; } = "";

    /// <summary>Eventos separados por espaço; "*" = todos.</summary>
    public string Eventos { get; set; } = "*";

    public bool Activa { get; set; } = true;
    public int FalhasSeguidas { get; set; }
    public DateTime? DesactivadaEm { get; set; }
    public string? MotivoDesactivacao { get; set; }

    public bool Subscreve(string evento) =>
        Eventos.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(e => e == "*" || e == evento);
}

public class WebhookEntrega
{
    public long Id { get; set; }
    public int SubscricaoId { get; set; }
    public WebhookSubscricao? Subscricao { get; set; }
    public Guid EventoId { get; set; }
    public string Evento { get; set; } = "";
    public string Payload { get; set; } = "";
    public EstadoEntrega Estado { get; set; } = EstadoEntrega.Pendente;
    public int Tentativas { get; set; }
    public DateTime? ProximaTentativa { get; set; }
    public int? CodigoResposta { get; set; }
    public string? Resposta { get; set; }
    public DateTime? EntregueEm { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum EstadoEntrega { Pendente = 0, Entregue = 1, Falhou = 2 }

public static class EventosWebhook
{
    public const string DocumentoEmitido = "documento.emitido";
    public const string DocumentoAnulado = "documento.anulado";
    public const string DocumentoEnviado = "documento.enviado";
    public const string PagamentoRecebido = "pagamento.recebido";
    public const string DocumentoVencido = "documento.vencido";
    public const string ClienteCriado = "cliente.criado";
    public const string Teste = "teste";

    public static readonly string[] Todos = [DocumentoEmitido, DocumentoAnulado, DocumentoEnviado, PagamentoRecebido, DocumentoVencido, ClienteCriado];
}
