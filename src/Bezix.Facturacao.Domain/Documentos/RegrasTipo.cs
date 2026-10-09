namespace Bezix.Facturacao.Domain.Documentos;

/// <summary>Regras que dependem só do tipo de documento (tabela da secção 4 da especificação).</summary>
public static class RegrasTipo
{
    /// <summary>Documentos que exigem NUIT do cliente (empresa sempre; particular acima do limite).</summary>
    public static bool ExigeNuit(TipoDocumento t) =>
        t is TipoDocumento.FT or TipoDocumento.FR or TipoDocumento.FG or TipoDocumento.NC or TipoDocumento.ND;

    /// <summary>Débito na conta corrente do cliente.</summary>
    public static bool Debita(TipoDocumento t) => t is TipoDocumento.FT or TipoDocumento.FR or TipoDocumento.FG or TipoDocumento.ND;

    /// <summary>Crédito na conta corrente.</summary>
    public static bool Credita(TipoDocumento t) => t is TipoDocumento.NC or TipoDocumento.RC or TipoDocumento.FR;

    public static bool TemVencimento(TipoDocumento t) => t is TipoDocumento.FT or TipoDocumento.FG or TipoDocumento.ND or TipoDocumento.CT;

    /// <summary>NC e ND referenciam obrigatoriamente o documento corrigido.</summary>
    public static bool ExigeOrigem(TipoDocumento t) => t is TipoDocumento.NC or TipoDocumento.ND;

    public static bool OrigemValida(TipoDocumento correccao, TipoDocumento origem) => correccao switch
    {
        TipoDocumento.NC => origem is TipoDocumento.FT or TipoDocumento.FR or TipoDocumento.FG,
        TipoDocumento.ND => origem is TipoDocumento.FT or TipoDocumento.FR or TipoDocumento.FG,
        _ => false,
    };

    /// <summary>Conversões permitidas: CT → FT/FR, GR → FT/FG.</summary>
    public static IReadOnlyList<TipoDocumento> ConverteEm(TipoDocumento t) => t switch
    {
        TipoDocumento.CT => [TipoDocumento.FT, TipoDocumento.FR],
        TipoDocumento.GR => [TipoDocumento.FT, TipoDocumento.FG],
        _ => [],
    };

    /// <summary>Tipos que se criam pelo editor (RC nasce do registo de pagamentos, na fase 4).</summary>
    public static readonly TipoDocumento[] Editaveis =
        [TipoDocumento.FT, TipoDocumento.FR, TipoDocumento.FG, TipoDocumento.NC, TipoDocumento.ND, TipoDocumento.CT, TipoDocumento.GR];

    public static string? MencaoObrigatoria(TipoDocumento t) => t switch
    {
        TipoDocumento.CT => "Este documento não serve de factura",
        TipoDocumento.GR => "Documento de transporte. Não serve de factura",
        _ => null,
    };
}
