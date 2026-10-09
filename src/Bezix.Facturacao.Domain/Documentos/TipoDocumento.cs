namespace Bezix.Facturacao.Domain.Documentos;

public enum TipoDocumento
{
    FT = 1, // Factura
    FR = 2, // Factura-Recibo
    FG = 3, // Factura global
    NC = 4, // Nota de crédito
    ND = 5, // Nota de débito
    RC = 6, // Recibo
    CT = 7, // Cotação / Proforma
    GR = 8, // Guia de remessa
}

public static class TipoDocumentoInfo
{
    public static string Nome(TipoDocumento tipo) => tipo switch
    {
        TipoDocumento.FT => "Factura",
        TipoDocumento.FR => "Factura-Recibo",
        TipoDocumento.FG => "Factura global",
        TipoDocumento.NC => "Nota de crédito",
        TipoDocumento.ND => "Nota de débito",
        TipoDocumento.RC => "Recibo",
        TipoDocumento.CT => "Cotação",
        TipoDocumento.GR => "Guia de remessa",
        _ => tipo.ToString(),
    };

    /// <summary>Só os documentos fiscais entram na exportação mensal para a AT.</summary>
    public static bool TemValorFiscal(TipoDocumento tipo) =>
        tipo is TipoDocumento.FT or TipoDocumento.FR or TipoDocumento.FG
            or TipoDocumento.NC or TipoDocumento.ND or TipoDocumento.RC;
}
