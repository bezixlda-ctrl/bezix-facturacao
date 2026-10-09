namespace Bezix.Facturacao.Web.Pages.Shared;

/// <summary>Dados para o partial _Paginacao: posição actual e como construir o URL de cada página.</summary>
public sealed record Paginacao(int Numero, int TotalPaginas, int Total, int Primeiro, int Ultimo, Func<int, string> Url, string? Alvo = null)
{
    public static Paginacao De<T>(Application.Comum.Pagina<T> p, Func<int, string> url, string? alvoHtmx = null) =>
        new(p.Numero, p.TotalPaginas, p.Total, p.Primeiro, p.Ultimo, url, alvoHtmx);
}
