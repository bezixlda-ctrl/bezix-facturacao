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
        const string assinatura = "\n\nPara qualquer esclarecimento, basta responder a este email.\n\nCumprimentos,\n{utilizador.nome}\n{empresa.nome}";
        const string contas = "\n\nPode pagar por transferência para:\n{empresa.contas}";
        ModeloComunicacao[] padrao =
        [
            new() { Codigo = "documento.FT", Nome = "Envio de factura", Assunto = "Factura {documento.numero} · {empresa.nome}",
                Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo a factura {documento.numero}, de {documento.data}, no valor de {documento.total}, com vencimento a {documento.vencimento}." + contas + assinatura },
            new() { Codigo = "documento.FG", Nome = "Envio de factura global", Assunto = "Factura global {documento.numero} · {empresa.nome}",
                Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo a factura global {documento.numero}, de {documento.data}, no valor de {documento.total}, com vencimento a {documento.vencimento}." + contas + assinatura },
            new() { Codigo = "documento.FR", Nome = "Envio de factura-recibo", Assunto = "Factura-recibo {documento.numero} · {empresa.nome}",
                Corpo = "Exmo(a). {cliente.contacto},\n\nAgradecemos o seu pagamento. Enviamos em anexo a factura-recibo {documento.numero}, de {documento.data}, no valor de {documento.total}." + assinatura },
            new() { Codigo = "documento.NC", Nome = "Envio de nota de crédito", Assunto = "Nota de crédito {documento.numero} · {empresa.nome}",
                Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo a nota de crédito {documento.numero}, de {documento.data}, no valor de {documento.total}." + assinatura },
            new() { Codigo = "documento.ND", Nome = "Envio de nota de débito", Assunto = "Nota de débito {documento.numero} · {empresa.nome}",
                Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo a nota de débito {documento.numero}, de {documento.data}, no valor de {documento.total}, com vencimento a {documento.vencimento}." + contas + assinatura },
            new() { Codigo = "documento.CT", Nome = "Envio de cotação", Assunto = "Cotação {documento.numero} · {empresa.nome}",
                Corpo = "Exmo(a). {cliente.contacto},\n\nConforme solicitado, enviamos em anexo a cotação {documento.numero}, no valor de {documento.total}, válida até {documento.vencimento}.\n\nPara avançar, basta confirmar por resposta a este email." + assinatura },
            new() { Codigo = "documento.RC", Nome = "Envio de recibo", Assunto = "Recibo {documento.numero} · {empresa.nome}",
                Corpo = "Exmo(a). {cliente.contacto},\n\nConfirmamos a recepção do seu pagamento de {documento.total}. Enviamos em anexo o recibo {documento.numero}, de {documento.data}.\n\nObrigado pela preferência." + assinatura },
            new() { Codigo = "extracto", Nome = "Envio de extracto de conta corrente", Assunto = "Extracto de conta corrente · {empresa.nome}",
                Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo o extracto da sua conta corrente de {extracto.periodo}.\n\nSaldo a esta data: {saldo.final}, dos quais {saldo.vencido} já vencidos." + contas + assinatura },
            new() { Codigo = "documento.GR", Nome = "Envio de guia de remessa", Assunto = "Guia de remessa {documento.numero} · {empresa.nome}",
                Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo a guia de remessa {documento.numero}, de {documento.data}, referente à mercadoria entregue." + assinatura },
        ];
        var novos = padrao.Where(m => !existentes.Contains(m.Codigo)).ToList();
        if (novos.Count == 0) return;
        db.ModelosComunicacao.AddRange(novos);
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
