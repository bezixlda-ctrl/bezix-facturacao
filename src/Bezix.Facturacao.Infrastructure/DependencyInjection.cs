using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Artigos;
using Bezix.Facturacao.Application.Automacao;
using Bezix.Facturacao.Application.Integracoes;
using Bezix.Facturacao.Application.Painel;
using Bezix.Facturacao.Application.Fiscal;
using Bezix.Facturacao.Infrastructure.Fiscal;
using Bezix.Facturacao.Infrastructure.Integracoes;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Application.Importacao;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Infrastructure.Email;
using Bezix.Facturacao.Infrastructure.Identidade;
using Bezix.Facturacao.Infrastructure.Pdf;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Bezix.Facturacao.Infrastructure.Servicos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bezix.Facturacao.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfraestrutura(this IServiceCollection services, IConfiguration config, string contentRoot)
    {
        var ligacao = config.GetConnectionString("BaseDados")
            ?? throw new InvalidOperationException("Falta a connection string 'BaseDados'.");

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<RastreioInterceptor>();
        services.AddScoped<ImutabilidadeInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o
            .UseSqlServer(ligacao, sql =>
            {
                // Produção no Winhost: SQL Server 2022 → nível de compatibilidade 160.
                sql.UseCompatibilityLevel(160);
                sql.EnableRetryOnFailure(3);
            })
            // A ordem conta: o rastreio preenche UpdatedAt antes da verificação de imutabilidade.
            .AddInterceptors(sp.GetRequiredService<RastreioInterceptor>(), sp.GetRequiredService<ImutabilidadeInterceptor>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ITransacoes, Transacoes>();
        services.AddScoped<IBloqueioSerie, BloqueioSerie>();

        services.Configure<OpcoesFicheiros>(config.GetSection("Ficheiros"));
        services.Configure<OpcoesInicializacao>(config.GetSection("Inicializacao"));
        services.Configure<OpcoesEmail>(config.GetSection("Email"));
        services.Configure<OpcoesCertificacao>(config.GetSection("Certificacao"));
        services.Configure<Backups.OpcoesBackups>(config.GetSection("Backups"));
        services.AddSingleton<Backups.IHostEnvironmentRaiz>(new Backups.HostEnvironmentRaiz(contentRoot));
        services.AddSingleton<Backups.IDestinoBackup>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<Backups.OpcoesBackups>>().Value;
            return o.Destino.ToUpperInvariant() switch
            {
                "S3" => new Backups.DestinoS3(o.S3),
                _ => new Backups.DestinoPasta(Path.GetFullPath(Path.Combine(contentRoot, string.IsNullOrWhiteSpace(o.Pasta) ? "App_Data/backups" : o.Pasta))),
            };
        });
        services.AddScoped<Backups.ServicoBackups>();
        services.AddSingleton<ArmazenamentoLocal>(sp => new ArmazenamentoLocal(sp.GetRequiredService<IOptions<OpcoesFicheiros>>(), contentRoot));
        services.AddSingleton<IArmazenamentoFicheiros>(sp => sp.GetRequiredService<ArmazenamentoLocal>());
        services.AddSingleton<IProcessadorImagem, ProcessadorImagemSkia>();
        services.AddSingleton<IGeradorPdf, GeradorPdfQuestPdf>();
        services.AddSingleton<IEnviadorEmail>(sp =>
        {
            var opcoes = sp.GetRequiredService<IOptions<OpcoesEmail>>();
            return string.IsNullOrWhiteSpace(opcoes.Value.Servidor)
                ? new EnviadorPastaLocal(opcoes, contentRoot)
                : new EnviadorSmtp(opcoes);
        });
        services.AddScoped<IServicoAuditoria, ServicoAuditoria>();
        services.AddScoped<IContextoSistema, ContextoSistema>();
        services.AddScoped<IAssinaturas, Assinaturas>();
        services.AddSingleton<IProtecaoSegredos, ProtecaoSegredos>();
        services.AddHttpClient(EnviadorWebhookHttp.NomeCliente, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(10);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Bezix-Facturacao-Webhooks/1.0");
        });
        services.AddSingleton<IEnviadorWebhook, EnviadorWebhookHttp>();
        services.AddSingleton<SinalFilas>();
        services.AddSingleton<ISinalFilas>(sp => sp.GetRequiredService<SinalFilas>());
        services.AddHostedService<ProcessadorFilas>();
        services.AddScoped<InicializadorBaseDados>();

        services.AddScoped<ServicoEmpresa>();
        services.AddScoped<ServicoContasBancarias>();
        services.AddScoped<ServicoTaxasIva>();
        services.AddScoped<ServicoSeries>();
        services.AddScoped<ServicoClientes>();
        services.AddScoped<ServicoArtigos>();
        services.AddScoped<ServicoImportacao>();
        services.AddScoped<NumeradorDocumentos>();
        services.AddScoped<ServicoDocumentos>();
        services.AddScoped<ServicoEmissao>();
        services.AddScoped<ServicoPdfDocumentos>();
        services.AddScoped<ServicoComunicacao>();
        services.AddScoped<ServicoContaCorrente>();
        services.AddScoped<ServicoRecibos>();
        services.AddScoped<ServicoPagamentosComunicados>();
        services.AddScoped<ServicoLinks>();
        services.AddScoped<ServicoPainel>();
        services.AddScoped<ServicoWebhooks>();
        services.AddScoped<IPublicadorEventos>(sp => sp.GetRequiredService<ServicoWebhooks>());
        services.AddScoped<ServicoChavesApi>();
        services.AddSingleton<IExportadorAT, ExportadorCsvProvisorio>();
        services.AddScoped<ServicoExportacaoAT>();
        services.AddScoped<Application.Relatorios.ServicoRelatorios>();
        services.AddScoped<ServicoLembretes>();
        services.AddScoped<ServicoRecorrencias>();
        services.AddScoped<ServicoTarefas>();
        services.AddScoped<ServicoUtilizadores>();
        services.AddScoped<ServicoPerfis>();
        return services;
    }
}
