using System.Security.Cryptography;
using System.Text;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Integracoes;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Integracoes;

/// <summary>
/// Chaves da API v1, no formato "bzx_{prefixo}_{segredo}". Guarda-se o prefixo (para encontrar a chave e a mostrar
/// nas listas) e o SHA-256 da chave completa. A chave completa só é mostrada no momento da criação.
/// </summary>
public sealed class ServicoChavesApi(IAppDbContext db, IServicoAuditoria auditoria, TimeProvider relogio)
{
    public Task<List<ChaveApi>> ListarAsync(CancellationToken ct = default) =>
        db.ChavesApi.AsNoTracking().OrderBy(c => c.RevogadaEm != null).ThenBy(c => c.Nome).ToListAsync(ct);

    public async Task<(Resultado Resultado, string? Chave)> CriarAsync(string nome, IReadOnlyCollection<string> escopos, CancellationToken ct = default)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(nome)) erros.Add("Dê um nome à chave (ex.: \"Loja online\").");
        if (escopos.Count == 0) erros.Add("Escolha pelo menos uma permissão.");
        if (escopos.Any(e => !EscoposApi.Existe(e))) erros.Add("Permissão desconhecida.");
        if (erros.Count > 0) return (Resultado.Falha([.. erros]), null);

        var prefixo = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var segredo = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(); // sem "_": o formato é bzx_prefixo_segredo
        var chave = $"bzx_{prefixo}_{segredo}";
        var c = new ChaveApi { Nome = nome.Trim(), Prefixo = prefixo, HashChave = Hash(chave), Escopos = string.Join(' ', escopos.Distinct()) };
        db.ChavesApi.Add(c);
        await db.SaveChangesAsync(ct);
        auditoria.Registar(nameof(ChaveApi), c.Id.ToString(), "ChaveApi.Criar", null, new { c.Nome, c.Prefixo, c.Escopos });
        await db.SaveChangesAsync(ct);
        return (Resultado.Ok(), chave);
    }

    public async Task<Resultado> RevogarAsync(int id, CancellationToken ct = default)
    {
        var c = await db.ChavesApi.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Resultado.Falha("Chave não encontrada.");
        if (c.RevogadaEm is not null) return Resultado.Falha("Esta chave já está revogada.");
        c.RevogadaEm = relogio.GetUtcNow().UtcDateTime;
        auditoria.Registar(nameof(ChaveApi), id.ToString(), "ChaveApi.Revogar", null, new { c.Nome, c.Prefixo });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>Valida a chave apresentada; null se não existir, estiver revogada ou não corresponder.</summary>
    public async Task<ChaveApi?> AutenticarAsync(string? chave, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chave)) return null;
        var partes = chave.Split('_');
        if (partes.Length != 3 || partes[0] != "bzx") return null;
        var c = await db.ChavesApi.SingleOrDefaultAsync(x => x.Prefixo == partes[1], ct);
        if (c is null || c.RevogadaEm is not null) return null;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(c.HashChave), Encoding.ASCII.GetBytes(Hash(chave)))) return null;

        // Último uso actualizado no máximo uma vez por minuto (evita escrever na BD em cada pedido).
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (c.UltimoUso is null || agora - c.UltimoUso > TimeSpan.FromMinutes(1))
        {
            c.UltimoUso = agora;
            await db.SaveChangesAsync(ct);
        }
        return c;
    }

    public static string Hash(string chave) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chave)));
}
