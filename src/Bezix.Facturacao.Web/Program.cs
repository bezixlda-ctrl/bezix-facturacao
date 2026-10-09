using System.Globalization;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Infrastructure;
using Bezix.Facturacao.Infrastructure.Identidade;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Bezix.Facturacao.Web.Formatacao;
using Bezix.Facturacao.Web.Seguranca;
using Bezix.Facturacao.Web.Sistema;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Serilog;
using Serilog.Sinks.MSSqlServer;
using System.Threading.RateLimiting;
using Bezix.Facturacao.Web.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(ctx.HostingEnvironment.ContentRootPath, "App_Data", "logs", "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31);

        var ligacao = ctx.Configuration.GetConnectionString("BaseDados");
        if (!string.IsNullOrEmpty(ligacao) && ctx.Configuration.GetValue("Registos:TabelaBaseDados", true))
        {
            // A tabela é criada pela migração do EF (RegistoLog); o sink só escreve.
            var colunas = new ColumnOptions();
            colunas.TimeStamp.DataType = System.Data.SqlDbType.DateTime2;
            colunas.TimeStamp.ConvertToUtc = true;
            colunas.Id.DataType = System.Data.SqlDbType.BigInt;
            cfg.WriteTo.MSSqlServer(ligacao,
                new MSSqlServerSinkOptions { TableName = "Registos", AutoCreateSqlTable = false },
                columnOptions: colunas,
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning);
        }
    });

    var servicos = builder.Services;
    // Produção: cookies só por HTTPS. Desenvolvimento local aceita HTTP (sem certificado instalado).
    var cookiesSoHttps = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    servicos.AddHttpContextAccessor();
    servicos.AddMemoryCache();
    servicos.AddScoped<IUtilizadorActual, UtilizadorActual>();
    servicos.AddScoped<IEnderecoPublico, EnderecoPublico>();

    // Chaves de cifra (cookies, antiforgery, segredos dos webhooks) guardadas em disco: sobrevivem à reciclagem do app pool.
    servicos.AddDataProtection()
        .SetApplicationName("Bezix.Facturacao")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "chaves")));
    servicos.AddInfraestrutura(builder.Configuration, builder.Environment.ContentRootPath);
    servicos.Configure<OpcoesJobs>(builder.Configuration.GetSection("Jobs"));

    servicos.AddIdentity<Utilizador, Perfil>(o =>
        {
            o.User.RequireUniqueEmail = true;
            o.Password.RequiredLength = 10;
            o.Password.RequireNonAlphanumeric = false;
            o.Password.RequireUppercase = true;
            o.Password.RequireLowercase = true;
            o.Password.RequireDigit = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            o.Lockout.AllowedForNewUsers = true;
            o.SignIn.RequireConfirmedAccount = false;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders()
        .AddClaimsPrincipalFactory<FabricaClaims>()
        .AddErrorDescriber<DescricaoErrosIdentidade>();

    // Mudanças de perfil, permissões ou desactivação chegam às sessões abertas em menos de 1 minuto.
    servicos.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

    servicos.ConfigureApplicationCookie(o =>
    {
        o.LoginPath = "/Conta/Entrar";
        o.LogoutPath = "/Conta/Sair";
        o.AccessDeniedPath = "/Conta/AcessoNegado";
        o.Cookie.Name = "bezix.sessao";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = cookiesSoHttps;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
    });

    servicos.AddSingleton<IAuthorizationPolicyProvider, PoliticaPermissaoProvider>();
    servicos.AddAuthentication().AddScheme<AuthenticationSchemeOptions, AutenticacaoApi>(AutenticacaoApi.Esquema, null);
    servicos.AddProblemDetails();
    servicos.AddOpenApi("v1", o => o.AddDocumentTransformer((doc, _, _) =>
    {
        doc.Info.Title = "Bezix Facturação API";
        doc.Info.Version = "v1";
        doc.Info.Description = "Autenticação: cabeçalho Authorization: Bearer <chave> (chaves em Administração → Integrações). "
            + "POST de documentos e recibos exigem o cabeçalho Idempotency-Key. Limite: 60 pedidos por minuto por chave. "
            + "Erros em application/problem+json. Valores em meticais (MZN).";
        return Task.CompletedTask;
    }));
    servicos.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.OnRejected = async (contexto, ct) =>
        {
            contexto.HttpContext.Response.Headers.RetryAfter = "60";
            await Results.Problem(statusCode: 429, title: "Demasiados pedidos", detail: "Limite de pedidos por minuto atingido. Tente daqui a um minuto.")
                .ExecuteAsync(contexto.HttpContext);
        };
        // API: 60 pedidos por minuto por chave (identificada pelo prefixo, antes da autenticação).
        o.AddPolicy("api", http =>
        {
            var auth = http.Request.Headers.Authorization.ToString();
            var partes = auth.StartsWith("Bearer bzx_", StringComparison.OrdinalIgnoreCase) ? auth["Bearer ".Length..].Split('_') : [];
            var chave = partes.Length >= 2 ? "chave:" + partes[1] : "ip:" + http.Connection.RemoteIpAddress;
            return RateLimitPartition.GetFixedWindowLimiter(chave, _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
        });
        // Página pública do cliente: 30 pedidos por minuto por IP.
        o.AddPolicy("publico", http => RateLimitPartition.GetFixedWindowLimiter("ip:" + http.Connection.RemoteIpAddress,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });

    servicos.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

    servicos.AddAntiforgery(o =>
    {
        o.HeaderName = "RequestVerificationToken"; // enviado pelo htmx em todos os pedidos
        o.Cookie.SecurePolicy = cookiesSoHttps;
    });

    servicos.AddRazorPages(o =>
        {
            // Tudo exige sessão (fallback policy), excepto estas páginas.
            o.Conventions.AllowAnonymousToPage("/Conta/Entrar");
            o.Conventions.AllowAnonymousToPage("/Conta/Entrar2FA");
            o.Conventions.AllowAnonymousToPage("/Conta/AcessoNegado");
            o.Conventions.AllowAnonymousToPage("/Error");
            o.Conventions.AllowAnonymousToFolder("/Publico");
        })
        .AddMvcOptions(o => o.ModelBinderProviders.Insert(0, new DecimalModelBinderProvider()));

    servicos.AddHealthChecks()
        .AddCheck<VerificacaoBaseDados>("baseDados")
        .AddCheck<VerificacaoDisco>("ficheiros");

    servicos.AddHsts(o =>
    {
        o.MaxAge = TimeSpan.FromDays(365);
        o.IncludeSubDomains = true;
    });

    var app = builder.Build();

    CultureInfo.DefaultThreadCurrentCulture = Formato.Cultura;
    CultureInfo.DefaultThreadCurrentUICulture = Formato.Cultura;

    // Dados iniciais (perfis, taxas, séries, primeiro administrador) são sempre garantidos no arranque.
    // As migrações podem ser aplicadas à mão com o script do CI (AplicarMigracoesNoArranque = false).
    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider.GetRequiredService<InicializadorBaseDados>()
            .ExecutarAsync(aplicarMigracoes: builder.Configuration.GetValue("BaseDados:AplicarMigracoesNoArranque", true));
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
        app.UseHttpsRedirection();
    }
    app.Use(async (http, next) =>
    {
        var h = http.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        await next();
    });

    app.UseRequestLocalization(new RequestLocalizationOptions
    {
        DefaultRequestCulture = new RequestCulture(Formato.Cultura),
        SupportedCultures = [Formato.Cultura],
        SupportedUICultures = [Formato.Cultura],
        RequestCultureProviders = [],
    });

    app.UseSerilogRequestLogging(o => o.GetLevel = (http, _, ex) =>
        ex is not null || http.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
        : http.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
        : Serilog.Events.LogEventLevel.Information);

    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapStaticAssets().AllowAnonymous();
    app.MapRazorPages().WithStaticAssets();
    app.MapEndpointsSistema();
    app.MapApiV1();
    app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous();
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/api/openapi/v1.json", "Bezix Facturação API v1");
        o.RoutePrefix = "api/docs";
        o.DocumentTitle = "Bezix Facturação · API";
    });

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "A aplicação terminou de forma inesperada.");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
