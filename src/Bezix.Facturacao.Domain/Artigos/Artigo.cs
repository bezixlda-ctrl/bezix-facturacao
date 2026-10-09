using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Artigos;

/// <summary>Produto ou serviço vendido.</summary>
public class Artigo : Entidade
{
    public string Codigo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public TipoArtigo Tipo { get; set; } = TipoArtigo.Servico;
    public string Unidade { get; set; } = "un";
    public decimal Preco { get; set; }

    /// <summary>
    /// Código da taxa de IVA (ex.: "NOR", "ISE"), não o Id. As taxas são versionadas por vigência:
    /// quando a taxa normal muda, o artigo passa a usar a nova sem ser editado.
    /// </summary>
    public string CodigoTaxaIva { get; set; } = "NOR";

    public bool Activo { get; set; } = true;
}

public enum TipoArtigo
{
    Produto = 0,
    Servico = 1,
}
