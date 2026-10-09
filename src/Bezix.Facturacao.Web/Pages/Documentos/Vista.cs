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

    /// <summary>Para documentos emitidos a débito, o estado mostrado é o de pagamento (Pago, Parcial, Vencido).</summary>
    public static (string Classe, string Texto) Estado(EstadoDocumento e, Application.Recebimentos.SituacaoDocumento? s) =>
        e == EstadoDocumento.Emitido && s is not null
            ? s.Estado switch
            {
                Domain.Recebimentos.EstadoPagamento.Pago => ("estado-pago", "Pago"),
                Domain.Recebimentos.EstadoPagamento.Parcial => ("estado-parcial", "Parcial"),
                Domain.Recebimentos.EstadoPagamento.Vencido => ("estado-vencido", "Vencido"),
                _ => ("estado-emitido", "Emitido"),
            }
            : Estado(e);

    public static string Icone(TipoDocumento t) => t switch
    {
        TipoDocumento.FT => "ph-file-text",
        TipoDocumento.FR => "ph-receipt",
        TipoDocumento.FG => "ph-files",
        TipoDocumento.NC => "ph-file-minus",
        TipoDocumento.ND => "ph-file-plus",
        TipoDocumento.CT => "ph-clipboard-text",
        TipoDocumento.GR => "ph-truck",
        _ => "ph-receipt",
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
