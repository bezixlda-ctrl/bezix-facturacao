using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Documentos;

namespace Bezix.Facturacao.Application.Documentos;

public sealed record LinhaRascunho(
    int? ArtigoId,
    string? Ref,
    string Descricao,
    string? Unidade,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal DescontoPct,
    string CodigoTaxaIva);

public sealed record DadosRascunho(
    TipoDocumento Tipo,
    int SerieId,
    int ClienteId,
    DateOnly Data,
    DateOnly? Vencimento,
    string Moeda,
    decimal Cambio,
    decimal DescontoGlobalPct,
    string? Observacoes,
    MetodoPagamento? MetodoPagamento,
    string? ReferenciaPagamento,
    string? LocalCarga,
    string? LocalDescarga,
    string? Matricula,
    int? DocOrigemId,
    string? MotivoCorreccao,
    IReadOnlyList<LinhaRascunho> Linhas,
    IReadOnlyList<int>? Origens = null);

public sealed record LinhaCalculo(LinhaRascunho Linha, decimal TaxaIvaPct, string? MotivoIsencao, decimal Base, decimal Iva, decimal TotalLinha);

public sealed record ResumoTaxa(decimal TaxaPct, string? MotivoIsencao, decimal Base, decimal Iva, decimal BaseMzn, decimal IvaMzn);

/// <summary>Resultado do cálculo de um rascunho (totais ao vivo no editor e recálculo definitivo na emissão).</summary>
public sealed record CalculoDocumento(
    IReadOnlyList<LinhaCalculo> Linhas,
    decimal Subtotal,
    decimal DescontoGlobalPct,
    decimal DescontoGlobal,
    decimal BaseTributavel,
    IReadOnlyList<ResumoTaxa> IvaPorTaxa,
    decimal TotalIva,
    decimal Total,
    decimal BaseTributavelMzn,
    decimal TotalIvaMzn,
    decimal TotalMzn,
    // Desconto efectivo total (linhas + global) face ao valor bruto, em %.
    decimal DescontoEfectivoPct,
    IReadOnlyList<string> Erros);

public sealed record FiltroDocumentos(
    TipoDocumento? Tipo = null,
    EstadoDocumento? Estado = null,
    int? ClienteId = null,
    string? Texto = null,
    DateOnly? De = null,
    DateOnly? Ate = null,
    int Pagina = 1,
    int Tamanho = Pagina<object>.TamanhoPadrao);

public sealed record DocumentoResumo(
    int Id,
    TipoDocumento Tipo,
    string? NumeroFormatado,
    EstadoDocumento Estado,
    DateOnly Data,
    DateOnly? Vencimento,
    int ClienteId,
    string ClienteNome,
    string Moeda,
    decimal Total,
    decimal TotalMzn);

public enum ResultadoEmissaoTipo { Emitido, AguardaAprovacao, Recusado }

public sealed record ResultadoEmissao(ResultadoEmissaoTipo Tipo, Resultado Resultado, string? NumeroFormatado = null)
{
    public static ResultadoEmissao Recusado(params string[] erros) => new(ResultadoEmissaoTipo.Recusado, Resultado.Falha(erros));
}
