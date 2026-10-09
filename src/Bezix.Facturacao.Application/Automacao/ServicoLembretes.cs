using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Automacao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Automacao;

public sealed record DadosPlanoLembretes(bool Activo, int DiasAntes, string DiasDepois, int MaxAvisos, int HoraEnvio, bool SoDiasUteis);

/// <summary>
/// Lembretes de cobrança: um aviso amigável antes do vencimento e até 3 avisos depois, com tom crescente.
/// Param sozinhos quando a factura fica paga. Respeitam as excepções do cliente (lembretes desligados, email de facturação, cópia).
/// </summary>
public sealed class ServicoLembretes(
    IAppDbContext db,
    ServicoContaCorrente contaCorrente,
    ServicoComunicacao comunicacao,
    ServicoDocumentos documentos,
    IUtilizadorActual utilizador,
    IServicoAuditoria auditoria,
    TimeProvider relogio)
{
    private static readonly TipoDocumento[] APagar = [TipoDocumento.FT, TipoDocumento.FG, TipoDocumento.ND];

    public async Task<PlanoLembretes> PlanoAsync(CancellationToken ct = default) =>
        await db.PlanosLembretes.AsNoTracking().SingleOrDefaultAsync(ct) ?? new PlanoLembretes();

    public async Task<Resultado> GuardarPlanoAsync(DadosPlanoLembretes d, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Automacao.Gerir)) return Resultado.Falha("O seu perfil não permite alterar automações.");
        var erros = new List<string>();
        if (d.DiasAntes is < 0 or > 60) erros.Add("Os dias antes do vencimento têm de estar entre 0 e 60.");
        if (d.MaxAvisos is < 0 or > 3) erros.Add("O máximo de avisos é 3.");
        if (d.HoraEnvio is < 0 or > 23) erros.Add("A hora tem de estar entre 0 e 23.");
        var dias = (d.DiasDepois ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (dias.Any(x => !int.TryParse(x, out var n) || n is < 1 or > 365)) erros.Add("Os dias depois do vencimento são números de 1 a 365 separados por vírgulas (ex.: 1, 15, 30).");
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        var p = await db.PlanosLembretes.SingleOrDefaultAsync(ct);
        if (p is null) { p = new PlanoLembretes { Id = PlanoLembretes.IdUnico }; db.PlanosLembretes.Add(p); }
        var antes = new { p.Activo, p.DiasAntes, p.DiasDepois, p.MaxAvisos, p.HoraEnvio, p.SoDiasUteis };
        p.Activo = d.Activo;
        p.DiasAntes = d.DiasAntes;
        p.DiasDepois = string.Join(",", dias.Select(int.Parse).Distinct().Order());
        p.MaxAvisos = d.MaxAvisos;
        p.HoraEnvio = d.HoraEnvio;
        p.SoDiasUteis = d.SoDiasUteis;
        auditoria.Registar(nameof(PlanoLembretes), "1", "Lembretes.Alterar", antes, new { p.Activo, p.DiasAntes, p.DiasDepois, p.MaxAvisos, p.HoraEnvio, p.SoDiasUteis });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>Envia os lembretes devidos neste momento. Chamado pelo jobs/tick.</summary>
    public async Task<int> ProcessarAsync(CancellationToken ct = default)
    {
        var plano = await PlanoAsync(ct);
        if (!plano.Activo) return 0;
        var local = relogio.GetUtcNow().UtcDateTime.AddHours(2);
        if (local.Hour < plano.HoraEnvio) return 0;
        if (plano.SoDiasUteis && local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return 0;
        var hoje = DateOnly.FromDateTime(local);
        var diasDepois = plano.ListaDiasDepois();
        var janela = hoje.AddDays(-(diasDepois.Count > 0 ? diasDepois.Max() + 30 : 30));

        var candidatos = await db.Documentos.AsNoTracking()
            .Where(d => d.Estado == EstadoDocumento.Emitido && APagar.Contains(d.TipoDoc) && d.Vencimento != null
                && d.Vencimento >= janela && d.Vencimento <= hoje.AddDays(plano.DiasAntes))
            .Join(db.Clientes.Where(c => c.Activo && c.LembretesActivos && (c.Email != null || c.EmailFacturacao != null)),
                d => d.ClienteId, c => c.Id, (d, c) => new { d.Id, Vencimento = d.Vencimento!.Value })
            .ToListAsync(ct);
        if (candidatos.Count == 0) return 0;

        var ids = candidatos.Select(c => c.Id).ToList();
        var situacao = await contaCorrente.SituacaoAsync(ids, ct);
        var enviados = (await db.LembretesEnviados.AsNoTracking().Where(l => ids.Contains(l.DocumentoId)).ToListAsync(ct))
            .GroupBy(l => l.DocumentoId).ToDictionary(g => g.Key, g => g.Select(l => l.Tipo).ToHashSet());

        var total = 0;
        foreach (var c in candidatos)
        {
            if (!situacao.TryGetValue(c.Id, out var s) || s.EmAberto <= 0) continue;
            var ja = enviados.GetValueOrDefault(c.Id) ?? [];
            var tipo = Devido(c.Vencimento, hoje, plano.DiasAntes, diasDepois, ja);
            if (tipo is null) continue;
            if (await EnviarAsync(c.Id, tipo.Value, ct)) total++;
        }
        return total;
    }

    /// <summary>Qual o lembrete devido hoje (no máximo um por documento e por dia; os avisos saem por ordem).</summary>
    public static TipoLembrete? Devido(DateOnly vencimento, DateOnly hoje, int diasAntes, IReadOnlyList<int> diasDepois, IReadOnlySet<TipoLembrete> jaEnviados)
    {
        if (hoje < vencimento)
            return diasAntes > 0 && hoje >= vencimento.AddDays(-diasAntes) && !jaEnviados.Contains(TipoLembrete.Antes) ? TipoLembrete.Antes : null;
        var avisosFeitos = jaEnviados.Count(t => t is TipoLembrete.Aviso1 or TipoLembrete.Aviso2 or TipoLembrete.Aviso3);
        if (avisosFeitos >= diasDepois.Count) return null;
        return hoje >= vencimento.AddDays(diasDepois[avisosFeitos]) ? (TipoLembrete)(avisosFeitos + 1) : null;
    }

    /// <summary>Botão "Enviar lembrete": envia já o aviso que corresponde à situação do documento.</summary>
    public async Task<Resultado> EnviarAgoraAsync(int documentoId, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Comunicacao.Enviar)) return Resultado.Falha("O seu perfil não permite enviar lembretes.");
        var doc = await documentos.ObterAsync(documentoId, ct);
        if (doc is null) return Resultado.Falha("Documento não encontrado.");
        var s = (await contaCorrente.SituacaoAsync([documentoId], ct)).GetValueOrDefault(documentoId);
        if (s is null || s.EmAberto <= 0) return Resultado.Falha("Este documento não tem valor em aberto.");

        var ja = (await db.LembretesEnviados.AsNoTracking().Where(l => l.DocumentoId == documentoId).Select(l => l.Tipo).ToListAsync(ct)).ToHashSet();
        var hoje = contaCorrente.Hoje();
        var tipo = doc.Vencimento is { } v && hoje <= v
            ? TipoLembrete.Antes
            : (TipoLembrete)Math.Min(3, ja.Count(t => t is TipoLembrete.Aviso1 or TipoLembrete.Aviso2 or TipoLembrete.Aviso3) + 1);
        return await EnviarAsync(documentoId, tipo, ct) ? Resultado.Ok() : Resultado.Falha("Não foi possível enviar: o cliente não tem email ou o envio falhou (fica na fila).");
    }

    public Task<List<LembreteEnviado>> HistoricoAsync(int documentoId, CancellationToken ct = default) =>
        db.LembretesEnviados.AsNoTracking().Where(l => l.DocumentoId == documentoId).OrderBy(l => l.Id).ToListAsync(ct);

    public static string CodigoModelo(TipoLembrete t) => t switch
    {
        TipoLembrete.Antes => "lembrete.antes",
        TipoLembrete.Aviso1 => "lembrete.aviso1",
        TipoLembrete.Aviso2 => "lembrete.aviso2",
        _ => "lembrete.aviso3",
    };

    private async Task<bool> EnviarAsync(int documentoId, TipoLembrete tipo, CancellationToken ct)
    {
        var (r, emailId) = await comunicacao.EnviarComModeloAsync(documentoId, CodigoModelo(tipo), ct);
        // Fica registado mesmo que o envio tenha ficado na fila: a fila repete; aqui não se volta a gerar o mesmo aviso.
        if (emailId is null) return false;
        db.LembretesEnviados.Add(new LembreteEnviado
        {
            DocumentoId = documentoId, Tipo = tipo, EnviadoEm = relogio.GetUtcNow().UtcDateTime, EmailId = emailId, EnviadoPor = utilizador.Email,
        });
        await db.SaveChangesAsync(ct);
        return r.Sucesso || emailId is not null;
    }
}
