using System.Collections.Concurrent;
using Bezix.Facturacao.Application.Abstracoes;

namespace Bezix.Facturacao.Tests.Integracao;

/// <summary>
/// Substitui o cliente HTTP dos webhooks: guarda cada pedido e responde com o código configurado para o URL
/// (200 por omissão). Cada teste usa o seu próprio URL, por isso os testes não interferem uns com os outros.
/// </summary>
public sealed class EnviadorWebhookFalso : IEnviadorWebhook
{
    public sealed record Pedido(string Url, string Corpo, IReadOnlyDictionary<string, string> Cabecalhos);

    private readonly ConcurrentQueue<Pedido> _pedidos = new();
    private readonly ConcurrentDictionary<string, int> _respostas = new();

    public void Responder(string url, int codigo) => _respostas[url] = codigo;

    public IReadOnlyList<Pedido> Para(string url) => _pedidos.Where(p => p.Url == url).ToList();

    public Task<RespostaHttp> EnviarAsync(string url, string corpo, IReadOnlyDictionary<string, string> cabecalhos, CancellationToken ct = default)
    {
        _pedidos.Enqueue(new Pedido(url, corpo, new Dictionary<string, string>(cabecalhos)));
        var codigo = _respostas.GetValueOrDefault(url, 200);
        return Task.FromResult(new RespostaHttp(codigo, codigo < 300 ? "ok" : "erro do destino", null));
    }
}
