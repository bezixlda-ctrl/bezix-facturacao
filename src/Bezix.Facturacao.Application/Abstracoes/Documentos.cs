using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;

namespace Bezix.Facturacao.Application.Abstracoes;

/// <summary>Transacção com repetição automática em falhas transitórias (o contexto é limpo antes de cada tentativa).</summary>
public interface ITransacoes
{
    Task<T> ExecutarAsync<T>(Func<CancellationToken, Task<T>> accao, CancellationToken ct = default);
}

/// <summary>
/// Bloqueia a série para escrita até ao fim da transacção (SQL Server: UPDLOCK, HOLDLOCK) e devolve-a actualizada.
/// Garante que dois utilizadores a emitir em simultâneo na mesma série não obtêm o mesmo número nem deixam saltos.
/// </summary>
public interface IBloqueioSerie
{
    Task<Serie> BloquearAsync(int serieId, CancellationToken ct = default);
}

public enum MarcaPdf
{
    Nenhuma,
    SegundaVia,
    Anulado,
    Rascunho,
}

/// <summary>Documento pago por um recibo (impresso no PDF do recibo).</summary>
public sealed record AlocacaoPdf(string Documento, DateOnly Data, decimal TotalMzn, decimal Valor);

public sealed record OpcoesPdf(MarcaPdf Marca, string? Utilizador, bool ValorPorExtenso = true)
{
    /// <summary>Só para recibos: documentos pagos e valor que ficou como adiantamento.</summary>
    public IReadOnlyList<AlocacaoPdf> Alocacoes { get; init; } = [];
    public decimal Adiantamento { get; init; }
}

public interface IGeradorPdf
{
    byte[] GerarDocumento(Documento documento, DadosEmitente emitente, Moeda moeda, OpcoesPdf opcoes);
    byte[] GerarExtracto(Recebimentos.Extracto extracto, DadosEmitente emitente, string? utilizador);
}

public sealed record AnexoEmail(string Nome, byte[] Conteudo, string TipoMime = "application/pdf");

public sealed record MensagemEmail(
    string Para,
    string? Cc,
    string? Bcc,
    string Assunto,
    string CorpoHtml,
    IReadOnlyList<AnexoEmail> Anexos);

/// <summary>Envio real (SMTP) ou, em desenvolvimento e testes, gravação em ficheiros .eml.</summary>
public interface IEnviadorEmail
{
    Task EnviarAsync(MensagemEmail mensagem, CancellationToken ct = default);
}
