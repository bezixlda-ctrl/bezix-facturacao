using System.Security.Claims;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Fiscal;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bezix.Facturacao.Infrastructure.Persistencia;

public sealed class OpcoesInicializacao
{
    /// <summary>Primeiro administrador, criado só se ainda não existir nenhum utilizador.</summary>
    public string? AdministradorEmail { get; set; }
    public string? AdministradorNome { get; set; }
    public string? AdministradorPassword { get; set; }
}

/// <summary>
/// Executado no arranque: aplica migrações e garante os dados mínimos (empresa, perfis, taxas, séries do ano).
/// É idempotente e nunca sobrepõe alterações feitas pelo Administrador, excepto dar ao perfil
/// Administrador as permissões novas que surjam em versões futuras.
/// </summary>
public sealed class InicializadorBaseDados(
    AppDbContext db,
    RoleManager<Perfil> perfis,
    UserManager<Utilizador> utilizadores,
    IOptions<OpcoesInicializacao> opcoes,
    TimeProvider relogio,
    ILogger<InicializadorBaseDados> log)
{
    public const string NuitPorPreencher = "000000000";

    public async Task ExecutarAsync(bool aplicarMigracoes = true, CancellationToken ct = default)
    {
        if (aplicarMigracoes && db.Database.IsRelational())
            await db.Database.MigrateAsync(ct);

        await GarantirEmpresaAsync(ct);
        await GarantirPerfisAsync();
        await GarantirTaxasAsync(ct);
        await GarantirSeriesDoAnoAsync(ct);
        await GarantirMoedasAsync(ct);
        await GarantirModelosAsync(ct);
        await GarantirPlanoLembretesAsync(ct);
        await GarantirAdministradorAsync();
    }

    private async Task GarantirEmpresaAsync(CancellationToken ct)
    {
        if (await db.Empresas.AnyAsync(ct)) return;
        db.Empresas.Add(new Empresa
        {
            Id = Empresa.IdUnico,
            Nome = "Bezix, Lda",
            Nuit = NuitPorPreencher,
            Endereco = "Por preencher",
            Cidade = "Maputo",
            Provincia = "Maputo Cidade",
        });
        await db.SaveChangesAsync(ct);
        log.LogInformation("Registo da empresa criado com dados provisórios.");
    }

    private async Task GarantirPerfisAsync()
    {
        foreach (var def in PerfisPadrao.Todos)
        {
            var perfil = await perfis.FindByNameAsync(def.Nome);
            if (perfil is null)
            {
                perfil = new Perfil(def.Nome) { Descricao = def.Descricao, Sistema = true, DescontoMaximoPct = def.DescontoMaximoPct };
                Verificar(await perfis.CreateAsync(perfil));
                foreach (var p in def.Permissoes)
                    Verificar(await perfis.AddClaimAsync(perfil, new Claim(Permissoes.ClaimType, p)));
                log.LogInformation("Perfil {Perfil} criado com {Total} permissões.", def.Nome, def.Permissoes.Count);
            }
            else if (def.Nome == PerfisPadrao.Administrador)
            {
                var actuais = (await perfis.GetClaimsAsync(perfil))
                    .Where(c => c.Type == Permissoes.ClaimType).Select(c => c.Value).ToHashSet();
                foreach (var p in def.Permissoes.Where(p => !actuais.Contains(p)))
                    Verificar(await perfis.AddClaimAsync(perfil, new Claim(Permissoes.ClaimType, p)));
            }
        }
    }

    private async Task GarantirTaxasAsync(CancellationToken ct)
    {
        if (await db.TaxasIva.AnyAsync(ct)) return;
        db.TaxasIva.AddRange(
            new TaxaIva
            {
                Codigo = "NOR",
                Descricao = "Taxa normal",
                Percentagem = 16m,
                VigenteDesde = new DateOnly(2023, 1, 1), // Lei n.º 22/2022
            },
            new TaxaIva
            {
                Codigo = "ISE",
                Descricao = "Isento",
                Percentagem = 0m,
                // Texto provisório: confirmar a menção exacta com o contabilista.
                MotivoIsencao = "Isento nos termos do artigo 9 do Código do IVA",
                VigenteDesde = new DateOnly(2023, 1, 1),
            });
        await db.SaveChangesAsync(ct);
    }

    private async Task GarantirSeriesDoAnoAsync(CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime);
        if (await db.Series.AnyAsync(s => s.Ano == hoje.Year, ct)) return;
        // Só no primeiro arranque do ano sem séries: abre a série "A" para cada tipo de documento.
        if (await db.Series.AnyAsync(ct)) return;
        foreach (var tipo in Enum.GetValues<TipoDocumento>())
        {
            db.Series.Add(new Serie
            {
                TipoDoc = tipo,
                Codigo = "A",
                Ano = hoje.Year,
                DataAbertura = new DateOnly(hoje.Year, 1, 1),
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task GarantirMoedasAsync(CancellationToken ct)
    {
        var existentes = await db.Moedas.Select(m => m.Codigo).ToListAsync(ct);
        Moeda[] padrao =
        [
            new() { Codigo = "MZN", Nome = "Metical", Simbolo = "MT", NomeSingular = "metical", NomePlural = "meticais", CentimoSingular = "centavo", CentimoPlural = "centavos", Ordem = 1 },
        ];
        var novas = padrao.Where(m => !existentes.Contains(m.Codigo)).ToList();
        if (novas.Count == 0) return;
        db.Moedas.AddRange(novas);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Textos dirigidos ao cliente: criados uma vez e depois editáveis (nunca sobrepostos).</summary>
    private async Task GarantirModelosAsync(CancellationToken ct)
    {
        var existentes = await db.ModelosComunicacao.Select(m => m.Codigo).ToListAsync(ct);
        var novos = Application.Comunicacao.ModelosPadrao.Todos.Where(m => !existentes.Contains(m.Codigo))
            .Select(m => new ModeloComunicacao { Codigo = m.Codigo, Nome = m.Nome, Assunto = m.Assunto, Corpo = m.Corpo, Canal = m.Canal, AnexarPdf = m.AnexarPdf })
            .ToList();
        if (novos.Count == 0) return;
        db.ModelosComunicacao.AddRange(novos);
        await db.SaveChangesAsync(ct);
    }

    private async Task GarantirPlanoLembretesAsync(CancellationToken ct)
    {
        if (await db.PlanosLembretes.AnyAsync(ct)) return;
        db.PlanosLembretes.Add(new Domain.Automacao.PlanoLembretes { Id = Domain.Automacao.PlanoLembretes.IdUnico });
        await db.SaveChangesAsync(ct);
    }

    private async Task GarantirAdministradorAsync()
    {
        if (await utilizadores.Users.AnyAsync()) return;

        var o = opcoes.Value;
        if (string.IsNullOrWhiteSpace(o.AdministradorEmail) || string.IsNullOrWhiteSpace(o.AdministradorPassword))
        {
            log.LogWarning("Não existe nenhum utilizador. Defina Inicializacao:AdministradorEmail e " +
                "Inicializacao:AdministradorPassword para criar o primeiro administrador.");
            return;
        }

        var admin = new Utilizador
        {
            UserName = o.AdministradorEmail,
            Email = o.AdministradorEmail,
            EmailConfirmed = true,
            Nome = o.AdministradorNome ?? "Administrador",
            CreatedAt = relogio.GetUtcNow().UtcDateTime,
        };
        Verificar(await utilizadores.CreateAsync(admin, o.AdministradorPassword));
        Verificar(await utilizadores.AddToRoleAsync(admin, PerfisPadrao.Administrador));
        log.LogInformation("Primeiro administrador criado: {Email}.", admin.Email);
    }

    private static void Verificar(IdentityResult r)
    {
        if (!r.Succeeded)
            throw new InvalidOperationException("Falha na inicialização: " + string.Join("; ", r.Errors.Select(e => e.Description)));
    }
}
