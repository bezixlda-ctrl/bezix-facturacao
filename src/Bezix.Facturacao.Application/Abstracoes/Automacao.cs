namespace Bezix.Facturacao.Application.Abstracoes;

/// <summary>
/// Execução em nome do sistema (jobs/tick: lembretes, recorrentes). Enquanto activo, o utilizador actual
/// é "sistema", com todas as permissões e sem limite de desconto. Fica registado assim na auditoria.
/// </summary>
public interface IContextoSistema
{
    bool Activo { get; }
    IDisposable Entrar();
}

public sealed class ContextoSistema : IContextoSistema
{
    private int _niveis;
    public bool Activo => _niveis > 0;

    public IDisposable Entrar()
    {
        _niveis++;
        return new Sair(() => _niveis--);
    }

    private sealed class Sair(Action accao) : IDisposable
    {
        private bool _feito;
        public void Dispose()
        {
            if (_feito) return;
            _feito = true;
            accao();
        }
    }
}

/// <summary>Publica eventos para os webhooks. As entregas ficam no contexto e são gravadas com o SaveChanges seguinte.</summary>
public interface IPublicadorEventos
{
    void Publicar(string evento, object dados);
}

/// <summary>Acorda o processador de filas (webhooks) depois de um commit. Sem processo vivo, o jobs/tick faz o mesmo.</summary>
public interface ISinalFilas
{
    void Sinalizar();
}

public interface IProtecaoSegredos
{
    string Cifrar(string texto);
    string Decifrar(string cifrado);
}

public sealed record RespostaHttp(int? Codigo, string? Corpo, string? Erro)
{
    public bool Sucesso => Codigo is >= 200 and < 300;
}

public interface IEnviadorWebhook
{
    Task<RespostaHttp> EnviarAsync(string url, string corpo, IReadOnlyDictionary<string, string> cabecalhos, CancellationToken ct = default);
}

/// <summary>Assinatura de email pessoal de um utilizador (guardada na tabela do Identity).</summary>
public interface IAssinaturas
{
    Task<string?> DoUtilizadorAsync(string utilizadorId, CancellationToken ct = default);
}

/// <summary>Endereço público do sistema para os links enviados aos clientes (configuração ou, em falta, o do pedido actual).</summary>
public interface IEnderecoPublico
{
    string? Base { get; }
}
