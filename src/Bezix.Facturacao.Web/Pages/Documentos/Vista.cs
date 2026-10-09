using Bezix.Facturacao.Domain.Documentos;

namespace Bezix.Facturacao.Web.Pages.Documentos;

/// <summary>Etiquetas de estado e nomes usados nas páginas de documentos.</summary>
public static class Vista
{
    public static (string Classe, string Texto) Estado(EstadoDocumento e) => e switch
    {
        EstadoDocumento.Rascunho => ("estado-rascunho", "Rascunho"),
        EstadoDocumento.AguardaAprovacao => ("estado-parcial", "Aguarda aprovação"),
        EstadoDocumento.Emitido => ("estado-emitido", "Emitido"),
        EstadoDocumento.Anulado => ("estado-anulado", "Anulado"),
        _ => ("estado-rascunho", e.ToString()),
    };

    public static string Icone(TipoDocumento t) => t switch
    {
        TipoDocumento.FT => "bi-file-earmark-text",
        TipoDocumento.FR => "bi-file-earmark-check",
        TipoDocumento.FG => "bi-files",
        TipoDocumento.NC => "bi-file-earmark-minus",
        TipoDocumento.ND => "bi-file-earmark-plus",
        TipoDocumento.CT => "bi-file-earmark-richtext",
        TipoDocumento.GR => "bi-truck",
        _ => "bi-receipt",
    };

    /// <summary>Texto do botão "Nova …".</summary>
    public static string Novo(TipoDocumento t) => t switch
    {
        TipoDocumento.FT => "Nova factura",
        TipoDocumento.FR => "Nova factura-recibo",
        TipoDocumento.FG => "Nova factura global",
        TipoDocumento.NC => "Nova nota de crédito",
        TipoDocumento.ND => "Nova nota de débito",
        TipoDocumento.CT => "Nova cotação",
        TipoDocumento.GR => "Nova guia de remessa",
        _ => "Novo documento",
    };
}
