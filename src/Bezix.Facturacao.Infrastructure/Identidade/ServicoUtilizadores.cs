using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Infrastructure.Identidade;

public sealed record UtilizadorResumo(
    string Id, string Nome, string Email, string? Perfil, bool Activo, bool DoisFactores, DateTime? UltimoLogin, bool Bloqueado);

public sealed record DadosUtilizador(string Nome, string Email, string Perfil, bool Activo);

public sealed class ServicoUtilizadores(
    UserManager<Utilizador> utilizadores,
    AppDbContext db,
    IServicoAuditoria auditoria,
    TimeProvider relogio)
{
    public const int MinimoAdministradores = 2;

    public async Task<List<UtilizadorResumo>> ListarAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow();
        var perfisPorUtilizador = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, r.Name }).ToListAsync(ct);

        var lista = await db.Users.AsNoTracking().OrderBy(u => u.Nome).ToListAsync(ct);
        return lista.Select(u => new UtilizadorResumo(
            u.Id, u.Nome, u.Email ?? "",
            perfisPorUtilizador.FirstOrDefault(p => p.UserId == u.Id)?.Name,
            u.Activo, u.TwoFactorEnabled, u.UltimoLogin,
            u.LockoutEnd is { } fim && fim > agora && u.Activo)).ToList();
    }

    public async Task<(Utilizador Utilizador, string? Perfil)?> ObterAsync(string id)
    {
        var u = await utilizadores.FindByIdAsync(id);
        if (u is null) return null;
        return (u, (await utilizadores.GetRolesAsync(u)).FirstOrDefault());
    }

    public async Task<int> ContarAdministradoresActivosAsync(string? excluirId = null)
    {
        var admins = await utilizadores.GetUsersInRoleAsync(PerfisPadrao.Administrador);
        return admins.Count(a => a.Activo && a.Id != excluirId);
    }

    public async Task<Resultado> CriarAsync(DadosUtilizador dados, string passwordInicial)
    {
        var erros = await ValidarAsync(dados);
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        var u = new Utilizador
        {
            UserName = dados.Email.Trim(),
            Email = dados.Email.Trim(),
            EmailConfirmed = true,
            Nome = dados.Nome.Trim(),
            Activo = dados.Activo,
            CreatedAt = relogio.GetUtcNow().UtcDateTime,
        };
        var r = await utilizadores.CreateAsync(u, passwordInicial);
        if (!r.Succeeded) return Resultado.Falha([.. r.Errors.Select(e => e.Description)]);
        await utilizadores.AddToRoleAsync(u, dados.Perfil);

        auditoria.Registar("Utilizador", u.Id, "Utilizador.Criar", null, new { u.Nome, u.Email, dados.Perfil, u.Activo });
        await db.SaveChangesAsync();
        return Resultado.Ok();
    }

    public async Task<Resultado> ActualizarAsync(string id, DadosUtilizador dados)
    {
        var u = await utilizadores.FindByIdAsync(id);
        if (u is null) return Resultado.Falha("Utilizador não encontrado.");

        var erros = await ValidarAsync(dados, id);
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        var perfilActual = (await utilizadores.GetRolesAsync(u)).FirstOrDefault();
        var eraAdminActivo = perfilActual == PerfisPadrao.Administrador && u.Activo;
        var ficaAdminActivo = dados.Perfil == PerfisPadrao.Administrador && dados.Activo;
        if (eraAdminActivo && !ficaAdminActivo && await ContarAdministradoresActivosAsync(excluirId: id) == 0)
            return Resultado.Falha("Este é o último administrador activo. Active ou crie outro administrador antes de o desactivar ou mudar de perfil.");

        var antes = new { u.Nome, u.Email, Perfil = perfilActual, u.Activo };
        u.Nome = dados.Nome.Trim();
        u.Email = dados.Email.Trim();
        u.UserName = dados.Email.Trim();
        u.Activo = dados.Activo;
        // Utilizador inactivo: bloqueio permanente. O carimbo de segurança novo termina as sessões abertas.
        u.LockoutEnabled = true;
        u.LockoutEnd = dados.Activo ? null : DateTimeOffset.MaxValue;
        var r = await utilizadores.UpdateAsync(u);
        if (!r.Succeeded) return Resultado.Falha([.. r.Errors.Select(e => e.Description)]);

        if (perfilActual != dados.Perfil)
        {
            if (perfilActual is not null) await utilizadores.RemoveFromRoleAsync(u, perfilActual);
            await utilizadores.AddToRoleAsync(u, dados.Perfil);
        }
        await utilizadores.UpdateSecurityStampAsync(u);

        auditoria.Registar("Utilizador", u.Id, "Utilizador.Alterar", antes, new { u.Nome, u.Email, dados.Perfil, u.Activo });
        await db.SaveChangesAsync();
        return Resultado.Ok();
    }

    public async Task<Resultado> RedefinirPasswordAsync(string id, string novaPassword)
    {
        var u = await utilizadores.FindByIdAsync(id);
        if (u is null) return Resultado.Falha("Utilizador não encontrado.");
        var token = await utilizadores.GeneratePasswordResetTokenAsync(u);
        var r = await utilizadores.ResetPasswordAsync(u, token, novaPassword);
        if (!r.Succeeded) return Resultado.Falha([.. r.Errors.Select(e => e.Description)]);
        await utilizadores.SetLockoutEndDateAsync(u, u.Activo ? null : DateTimeOffset.MaxValue);
        await utilizadores.ResetAccessFailedCountAsync(u);

        auditoria.Registar("Utilizador", u.Id, "Utilizador.RedefinirPassword", null, new { u.Email });
        await db.SaveChangesAsync();
        return Resultado.Ok();
    }

    private async Task<List<string>> ValidarAsync(DadosUtilizador d, string? id = null)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Nome)) erros.Add("Indique o nome.");
        if (string.IsNullOrWhiteSpace(d.Email) || !d.Email.Contains('@')) erros.Add("Indique um email válido.");
        else
        {
            var existente = await utilizadores.FindByEmailAsync(d.Email.Trim());
            if (existente is not null && existente.Id != id) erros.Add("Já existe um utilizador com este email.");
        }
        if (!await db.Roles.AnyAsync(r => r.Name == d.Perfil)) erros.Add("Escolha um perfil.");
        return erros;
    }
}
