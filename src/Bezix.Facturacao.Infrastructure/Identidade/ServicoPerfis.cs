using System.Security.Claims;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Infrastructure.Identidade;

public sealed record PerfilResumo(string Id, string Nome, string? Descricao, bool Sistema, decimal? DescontoMaximoPct, int Utilizadores, int Permissoes);

public sealed record DadosPerfil(string Nome, string? Descricao, decimal? DescontoMaximoPct, IReadOnlyCollection<string> Permissoes);

public sealed class ServicoPerfis(
    RoleManager<Perfil> perfis,
    UserManager<Utilizador> utilizadores,
    AppDbContext db,
    IServicoAuditoria auditoria)
{
    public async Task<List<PerfilResumo>> ListarAsync(CancellationToken ct = default)
    {
        var lista = await db.Roles.AsNoTracking()
            .Select(r => new PerfilResumo(
                r.Id, r.Name!, r.Descricao, r.Sistema, r.DescontoMaximoPct,
                db.UserRoles.Count(ur => ur.RoleId == r.Id),
                db.RoleClaims.Count(c => c.RoleId == r.Id && c.ClaimType == Permissoes.ClaimType)))
            .ToListAsync(ct);
        // Mantém a ordem dos perfis padrão (do mais ao menos privilegiado); perfis novos no fim.
        var ordem = PerfisPadrao.Todos.Select(p => p.Nome).ToList();
        return lista.OrderBy(p => ordem.IndexOf(p.Nome) is var i && i < 0 ? int.MaxValue : i).ThenBy(p => p.Nome).ToList();
    }

    public async Task<(Perfil Perfil, HashSet<string> Permissoes)?> ObterAsync(string id)
    {
        var perfil = await perfis.FindByIdAsync(id);
        if (perfil is null) return null;
        var claims = await perfis.GetClaimsAsync(perfil);
        return (perfil, claims.Where(c => c.Type == Permissoes.ClaimType).Select(c => c.Value).ToHashSet());
    }

    public async Task<Resultado> CriarAsync(DadosPerfil dados)
    {
        var erros = Validar(dados);
        if (await perfis.RoleExistsAsync(dados.Nome.Trim())) erros.Add("Já existe um perfil com este nome.");
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        var perfil = new Perfil(dados.Nome.Trim()) { Descricao = dados.Descricao?.Trim(), DescontoMaximoPct = dados.DescontoMaximoPct };
        var r = await perfis.CreateAsync(perfil);
        if (!r.Succeeded) return Resultado.Falha([.. r.Errors.Select(e => e.Description)]);
        foreach (var p in dados.Permissoes.Distinct())
            await perfis.AddClaimAsync(perfil, new Claim(Permissoes.ClaimType, p));

        auditoria.Registar("Perfil", perfil.Id, "Perfil.Criar", null, new { perfil.Name, dados.DescontoMaximoPct, Permissoes = dados.Permissoes.Order() });
        await db.SaveChangesAsync();
        return Resultado.Ok();
    }

    public async Task<Resultado> ActualizarAsync(string id, DadosPerfil dados)
    {
        var perfil = await perfis.FindByIdAsync(id);
        if (perfil is null) return Resultado.Falha("Perfil não encontrado.");

        var erros = Validar(dados);
        if (perfil.Sistema && perfil.Name != dados.Nome.Trim()) erros.Add("Os perfis do sistema não podem mudar de nome.");
        if (!perfil.Sistema && perfil.Name != dados.Nome.Trim() && await perfis.RoleExistsAsync(dados.Nome.Trim()))
            erros.Add("Já existe um perfil com este nome.");
        if (perfil.Name == PerfisPadrao.Administrador && dados.Permissoes.Count != Permissoes.Todas.Count)
            erros.Add("O perfil Administrador tem sempre todas as permissões, para que nunca se perca o acesso à gestão.");
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        var actuais = (await perfis.GetClaimsAsync(perfil)).Where(c => c.Type == Permissoes.ClaimType).ToList();
        var antes = new { perfil.Name, perfil.Descricao, perfil.DescontoMaximoPct, Permissoes = actuais.Select(c => c.Value).Order() };

        perfil.Name = dados.Nome.Trim();
        perfil.Descricao = dados.Descricao?.Trim();
        perfil.DescontoMaximoPct = dados.DescontoMaximoPct;
        await perfis.UpdateAsync(perfil);

        var novas = dados.Permissoes.ToHashSet();
        foreach (var c in actuais.Where(c => !novas.Contains(c.Value)))
            await perfis.RemoveClaimAsync(perfil, c);
        foreach (var p in novas.Where(p => actuais.All(c => c.Value != p)))
            await perfis.AddClaimAsync(perfil, new Claim(Permissoes.ClaimType, p));

        // As permissões vão no cookie: renovar o carimbo obriga as sessões abertas a recarregá-las.
        foreach (var u in await utilizadores.GetUsersInRoleAsync(perfil.Name))
            await utilizadores.UpdateSecurityStampAsync(u);

        auditoria.Registar("Perfil", perfil.Id, "Perfil.Alterar", antes,
            new { perfil.Name, perfil.Descricao, perfil.DescontoMaximoPct, Permissoes = novas.Order() });
        await db.SaveChangesAsync();
        return Resultado.Ok();
    }

    public async Task<Resultado> ApagarAsync(string id)
    {
        var perfil = await perfis.FindByIdAsync(id);
        if (perfil is null) return Resultado.Falha("Perfil não encontrado.");
        if (perfil.Sistema) return Resultado.Falha("Os perfis do sistema não podem ser apagados.");
        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == id))
            return Resultado.Falha("Há utilizadores com este perfil. Mude-lhes o perfil antes de o apagar.");

        await perfis.DeleteAsync(perfil);
        auditoria.Registar("Perfil", id, "Perfil.Apagar", new { perfil.Name }, null);
        await db.SaveChangesAsync();
        return Resultado.Ok();
    }

    private static List<string> Validar(DadosPerfil d)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Nome)) erros.Add("Indique o nome do perfil.");
        if (d.DescontoMaximoPct is < 0 or > 100) erros.Add("O desconto máximo tem de estar entre 0 e 100%.");
        var desconhecidas = d.Permissoes.Where(p => !Permissoes.Existe(p)).ToList();
        if (desconhecidas.Count > 0) erros.Add("Permissões desconhecidas: " + string.Join(", ", desconhecidas));
        return erros;
    }
}
