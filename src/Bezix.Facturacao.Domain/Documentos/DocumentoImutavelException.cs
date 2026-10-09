namespace Bezix.Facturacao.Domain.Documentos;

/// <summary>Tentativa de alterar ou apagar um documento emitido (ou as suas linhas).</summary>
public sealed class DocumentoImutavelException(int documentoId, string detalhe)
    : InvalidOperationException($"O documento {documentoId} está emitido e não pode ser alterado ({detalhe}). Corrija com nota de crédito/débito ou anulação.")
{
    public int DocumentoId { get; } = documentoId;
}
