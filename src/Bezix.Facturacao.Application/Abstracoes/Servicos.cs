namespace Bezix.Facturacao.Application.Abstracoes;

/// <summary>Utilizador do pedido actual (vazio em jobs e no arranque).</summary>
public interface IUtilizadorActual
{
    string? Id { get; }
    string? Email { get; }
    string? Ip { get; }
    bool TemPermissao(string permissao);
}

/// <summary>Ficheiros guardados em App_Data/uploads, fora de wwwroot.</summary>
public interface IArmazenamentoFicheiros
{
    Task<string> GuardarAsync(string pasta, string extensao, byte[] conteudo, CancellationToken ct = default);
    Stream? Abrir(string caminhoRelativo);
    void Apagar(string caminhoRelativo);
}

public interface IProcessadorImagem
{
    /// <summary>Valida o tipo real pelo conteúdo (não pela extensão) e redimensiona para a largura máxima.</summary>
    ResultadoImagem ProcessarLogotipo(byte[] conteudo, int larguraMaxima);
}

public sealed record ResultadoImagem(bool Valida, byte[]? Png, string? Erro)
{
    public static ResultadoImagem Invalida(string erro) => new(false, null, erro);
}

public interface IServicoAuditoria
{
    /// <summary>
    /// Acrescenta um registo ao contexto; é gravado no próximo SaveChanges.
    /// <paramref name="utilizador"/> substitui o utilizador da sessão (ex.: tentativas de login, ainda sem sessão).
    /// </summary>
    void Registar(string entidade, string? entidadeId, string accao, object? antes = null, object? depois = null, string? utilizador = null);
}
