namespace Bezix.Facturacao.Application.Comum;

/// <summary>Página de resultados. Listagens nunca carregam tudo para memória (limite de memória do Winhost).</summary>
public sealed record Pagina<T>(IReadOnlyList<T> Itens, int Total, int Numero, int Tamanho)
{
    public const int TamanhoPadrao = 25;
    public const int TamanhoMaximo = 100;

    public int TotalPaginas => Tamanho == 0 ? 0 : (int)Math.Ceiling(Total / (double)Tamanho);
    public bool TemAnterior => Numero > 1;
    public bool TemSeguinte => Numero < TotalPaginas;
    public int Primeiro => Total == 0 ? 0 : (Numero - 1) * Tamanho + 1;
    public int Ultimo => Math.Min(Numero * Tamanho, Total);

    public static (int Numero, int Tamanho) Normalizar(int numero, int tamanho) =>
        (Math.Max(1, numero), tamanho is < 1 or > TamanhoMaximo ? TamanhoPadrao : tamanho);
}
