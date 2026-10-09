namespace Bezix.Facturacao.Application.Comum;

/// <summary>Resultado de um caso de uso: sucesso ou lista de erros de validação legíveis pelo utilizador.</summary>
public sealed class Resultado
{
    public bool Sucesso => Erros.Count == 0;
    public IReadOnlyList<string> Erros { get; }

    private Resultado(IReadOnlyList<string> erros) => Erros = erros;

    public static Resultado Ok() => new([]);
    public static Resultado Falha(params string[] erros) => new(erros);
}
