using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Configuracao;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Bezix.Facturacao.Web.Sistema;

public sealed class OpcoesJobs
{
    /// <summary>Segredo partilhado com o Scheduled Task Tool do Winhost. Vazio = endpoint desactivado.</summary>
    public string? Token { get; set; }
}

public static class EndpointsSistema
{
    public static void MapEndpointsSistema(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = EscreverSaude }).AllowAnonymous();

        // O Winhost não tem processos permanentes: o Scheduled Task Tool chama este endereço a cada 15–60 min
        // para correr lembretes, recorrentes, webhooks e backups. O Scheduled Task Tool só faz GET e não
        // envia cabeçalhos, por isso o token também é aceite na query string.
        app.MapMethods("/internal/jobs/tick", ["GET", "POST"], async (HttpContext http, Microsoft.Extensions.Options.IOptions<OpcoesJobs> opcoes,
            Application.Automacao.ServicoTarefas tarefas, ILogger<OpcoesJobs> log) =>
        {
            if (!TokenValido(http, opcoes.Value.Token)) return Results.NotFound();

            var inicio = DateTime.UtcNow;
            var resultados = await tarefas.ExecutarAsync(http.RequestAborted);
            log.LogInformation("jobs/tick executado: {Resultados}", string.Join("; ", resultados.Select(r => $"{r.Tarefa}: {r.Resultado}")));
            return Results.Ok(new { executadoEm = inicio, tarefas = resultados });
        }).AllowAnonymous().DisableAntiforgery();

        // Logótipo servido a partir de App_Data (fora de wwwroot). Público: aparece no ecrã de entrada
        // e, mais tarde, na página do documento partilhada com o cliente.
        app.MapGet("/marca/logotipo", async (ServicoEmpresa empresas, IArmazenamentoFicheiros ficheiros, HttpContext http) =>
        {
            var empresa = await empresas.ObterAsync();
            if (empresa.LogoPath is null) return Results.NotFound();
            var stream = ficheiros.Abrir(empresa.LogoPath);
            if (stream is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Stream(stream, "image/png");
        }).AllowAnonymous();
    }

    private static bool TokenValido(HttpContext http, string? esperado)
    {
        if (string.IsNullOrWhiteSpace(esperado)) return false;
        var recebido = http.Request.Headers["X-Jobs-Token"].FirstOrDefault() ?? http.Request.Query["token"].FirstOrDefault();
        if (recebido is null) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebido), Encoding.UTF8.GetBytes(esperado));
    }

    private static Task EscreverSaude(HttpContext http, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport relatorio)
    {
        http.Response.ContentType = "application/json; charset=utf-8";
        return http.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            estado = relatorio.Status.ToString(),
            duracaoMs = (int)relatorio.TotalDuration.TotalMilliseconds,
            verificacoes = relatorio.Entries.ToDictionary(e => e.Key, e => new { estado = e.Value.Status.ToString(), e.Value.Description }),
        }));
    }
}
