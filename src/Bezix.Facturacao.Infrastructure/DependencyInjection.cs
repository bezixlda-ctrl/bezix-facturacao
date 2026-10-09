using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Infrastructure.Identidade;
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
        services.AddDbContext<AppDbContext>((sp, o) => o
            .UseSqlServer(ligacao, sql =>
            {
                // Produção no Winhost: SQL Server 2022 → nível de compatibilidade 160.
                sql.UseCompatibilityLevel(160);
                sql.EnableRetryOnFailure(3);
            })
            .AddInterceptors(sp.GetRequiredService<RastreioInterceptor>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<OpcoesFicheiros>(config.GetSection("Ficheiros"));
        services.Configure<OpcoesInicializacao>(config.GetSection("Inicializacao"));
        services.AddSingleton<ArmazenamentoLocal>(sp => new ArmazenamentoLocal(sp.GetRequiredService<IOptions<OpcoesFicheiros>>(), contentRoot));
        services.AddSingleton<IArmazenamentoFicheiros>(sp => sp.GetRequiredService<ArmazenamentoLocal>());
        services.AddSingleton<IProcessadorImagem, ProcessadorImagemSkia>();
        services.AddScoped<IServicoAuditoria, ServicoAuditoria>();
        services.AddScoped<InicializadorBaseDados>();

        services.AddScoped<ServicoEmpresa>();
        services.AddScoped<ServicoContasBancarias>();
        services.AddScoped<ServicoTaxasIva>();
        services.AddScoped<ServicoSeries>();
        services.AddScoped<ServicoUtilizadores>();
        services.AddScoped<ServicoPerfis>();
        return services;
    }
}
