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

        // Backup diário (decisão D23): chamado pela segunda tarefa agendada, de hora a hora ou uma vez por dia. Só faz o backup
        // se o último tiver mais de Backups:IntervaloHoras; "forcar=true" faz já. Corre até ao fim mesmo que quem chamou desista.
        app.MapMethods("/internal/jobs/backup", ["GET", "POST"], async (HttpContext http, Microsoft.Extensions.Options.IOptions<OpcoesJobs> opcoes,
            Infrastructure.Backups.ServicoBackups backups, bool forcar = false) =>
        {
            if (!TokenValido(http, opcoes.Value.Token)) return Results.NotFound();
            try
            {
                var r = await backups.ExecutarAsync(forcar, CancellationToken.None);
                return Results.Ok(r);
            }
            catch (Exception ex)
            {
                return Results.Problem(statusCode: 500, title: "O backup falhou", detail: ex.Message);
            }
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

        // Carimbo e assinatura: nunca públicos (só quem gere as configurações os vê fora dos PDFs).
        app.MapGet("/marca/{tipo:regex(^(carimbo|assinatura)$)}", async (string tipo, ServicoEmpresa empresas, IArmazenamentoFicheiros ficheiros, HttpContext http) =>
        {
            var empresa = await empresas.ObterAsync();
            var caminho = tipo == "carimbo" ? empresa.CarimboPath : empresa.AssinaturaPath;
            var stream = caminho is null ? null : ficheiros.Abrir(caminho);
            if (stream is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.Stream(stream, "image/png");
        }).RequireAuthorization(Domain.Seguranca.Permissoes.Configuracoes.Gerir);
    }

    private static bool TokenValido(HttpContext http, string? esperado)
    {
        if (string.IsNullOrWhiteSpace(esperado)) return false;
        var recebido = http.Request.Headers["X-Jobs-Token"].FirstOrDefault() ?? http.Request.Query["token"].FirstOrDefault();
        if (recebido is null) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebido), Encoding.UTF8.GetBytes(esperado));
    }

    private static readonly JsonSerializerOptions JsonSaude = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static Task EscreverSaude(HttpContext http, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport relatorio)
    {
        http.Response.ContentType = "application/json; charset=utf-8";
        return http.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            estado = relatorio.Status.ToString(),
            duracaoMs = (int)relatorio.TotalDuration.TotalMilliseconds,
            verificacoes = relatorio.Entries.ToDictionary(e => e.Key, e => new { estado = e.Value.Status.ToString(), descricao = e.Value.Description }),
        }, JsonSaude));
    }
}
