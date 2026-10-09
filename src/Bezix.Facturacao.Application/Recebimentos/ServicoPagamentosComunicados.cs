using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Recebimentos;

public sealed record PagamentoComunicadoVista(
    int Id, int ClienteId, string Cliente, int? DocumentoId, string? Documento, decimal Valor, DateOnly Data, string? Referencia,
    string? Mensagem, bool TemComprovativo, EstadoPagamentoComunicado Estado, DateTime ComunicadoEm, string? DecididoPor, string? MotivoRejeicao, int? ReciboId);

/// <summary>Pagamentos comunicados pelos clientes: a Tesouraria confere o comprovativo e emite o recibo, ou rejeita.</summary>
public sealed class ServicoPagamentosComunicados(IAppDbContext db, IArmazenamentoFicheiros ficheiros, IUtilizadorActual utilizador,
    IServicoAuditoria auditoria, TimeProvider relogio)
{
    public Task<int> ContarPorValidarAsync(CancellationToken ct = default) =>
        db.PagamentosComunicados.AsNoTracking().CountAsync(p => p.Estado == EstadoPagamentoComunicado.PorValidar, ct);

    public Task<List<PagamentoComunicadoVista>> ListarAsync(bool soPorValidar, CancellationToken ct = default)
    {
        var q = db.PagamentosComunicados.AsNoTracking();
        if (soPorValidar) q = q.Where(p => p.Estado == EstadoPagamentoComunicado.PorValidar);
        return Projectar(q.OrderBy(p => p.Estado).ThenByDescending(p => p.CreatedAt).Take(200)).ToListAsync(ct);
    }

    public Task<PagamentoComunicadoVista?> ObterAsync(int id, CancellationToken ct = default) =>
        Projectar(db.PagamentosComunicados.AsNoTracking().Where(p => p.Id == id)).SingleOrDefaultAsync(ct);

    public async Task<(Stream Conteudo, string Tipo)?> ComprovativoAsync(int id, CancellationToken ct = default)
    {
        var p = await db.PagamentosComunicados.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p?.ComprovativoPath is null) return null;
        var s = ficheiros.Abrir(p.ComprovativoPath);
        return s is null ? null : (s, p.ComprovativoTipo ?? "application/octet-stream");
    }

    public async Task<Resultado> RejeitarAsync(int id, string? motivo, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Recibos.ValidarPagamentoComunicado)) return Resultado.Falha("O seu perfil não permite validar pagamentos.");
        if (string.IsNullOrWhiteSpace(motivo)) return Resultado.Falha("Indique o motivo da rejeição.");
        var p = await db.PagamentosComunicados.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null || p.Estado != EstadoPagamentoComunicado.PorValidar) return Resultado.Falha("Este pagamento já foi tratado.");
        p.Estado = EstadoPagamentoComunicado.Rejeitado;
        p.MotivoRejeicao = motivo.Trim();
        p.DecididoPor = utilizador.Email;
        p.DecididoEm = relogio.GetUtcNow().UtcDateTime;
        auditoria.Registar(nameof(PagamentoComunicado), id.ToString(), "PagamentoComunicado.Rejeitar", null, new { p.Valor, motivo });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    private IQueryable<PagamentoComunicadoVista> Projectar(IQueryable<PagamentoComunicado> q) =>
        from p in q
        join c in db.Clientes on p.ClienteId equals c.Id
        join d in db.Documentos on p.DocumentoId equals d.Id into ds
        from d in ds.DefaultIfEmpty()
        select new PagamentoComunicadoVista(p.Id, p.ClienteId, c.Nome, p.DocumentoId, d.NumeroFormatado, p.Valor, p.Data, p.Referencia, p.Mensagem,
            p.ComprovativoPath != null, p.Estado, p.CreatedAt, p.DecididoPor, p.MotivoRejeicao, p.ReciboId);
}
