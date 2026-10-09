using System.Text;
using System.Threading.Channels;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Integracoes;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bezix.Facturacao.Infrastructure.Integracoes;

public sealed class ProtecaoSegredos(IDataProtectionProvider fornecedor) : IProtecaoSegredos
{
    private readonly IDataProtector _protector = fornecedor.CreateProtector("Bezix.Facturacao.Segredos.v1");
    public string Cifrar(string texto) => _protector.Protect(texto);
    public string Decifrar(string cifrado) => _protector.Unprotect(cifrado);
}

public sealed class EnviadorWebhookHttp(IHttpClientFactory fabrica) : IEnviadorWebhook
{
    public const string NomeCliente = "webhooks";

    public async Task<RespostaHttp> EnviarAsync(string url, string corpo, IReadOnlyDictionary<string, string> cabecalhos, CancellationToken ct = default)
    {
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
            foreach (var (k, v) in cabecalhos) pedido.Headers.TryAddWithoutValidation(k, v);
            using var resposta = await fabrica.CreateClient(NomeCliente).SendAsync(pedido, ct);
            var texto = await resposta.Content.ReadAsStringAsync(ct);
            return new RespostaHttp((int)resposta.StatusCode, texto.Length > 1000 ? texto[..1000] : texto, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new RespostaHttp(null, null, ex.Message);
        }
    }
}

/// <summary>Sinal para o processador de filas (um canal com no máximo um aviso pendente).</summary>
public sealed class SinalFilas : ISinalFilas
{
    private readonly Channel<bool> _canal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    public void Sinalizar() => _canal.Writer.TryWrite(true);
    public ChannelReader<bool> Leitor => _canal.Reader;
}

/// <summary>
/// Entrega webhooks pouco depois de um evento, enquanto a aplicação está viva. Quando o app pool recicla,
/// o jobs/tick entrega o que tiver ficado pendente. Espera 2 s depois do sinal para o commit de quem publicou terminar.
/// </summary>
public sealed class ProcessadorFilas(SinalFilas sinal, IServiceScopeFactory fabrica, ILogger<ProcessadorFilas> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in sinal.Leitor.ReadAllAsync(ct))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                try
                {
                    using var scope = fabrica.CreateScope();
                    using var _s = scope.ServiceProvider.GetRequiredService<IContextoSistema>().Entrar();
                    await scope.ServiceProvider.GetRequiredService<ServicoWebhooks>().EntregarPendentesAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Falha a entregar webhooks pendentes; o jobs/tick tenta de novo.");
                }
            }
        }
        catch (OperationCanceledException) { }
    }
}

public sealed class Assinaturas(AppDbContext db) : IAssinaturas
{
    public Task<string?> DoUtilizadorAsync(string utilizadorId, CancellationToken ct = default) =>
        db.Users.AsNoTracking().Where(u => u.Id == utilizadorId).Select(u => u.AssinaturaEmail).SingleOrDefaultAsync(ct);
}
