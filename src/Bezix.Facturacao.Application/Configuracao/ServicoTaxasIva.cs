using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Fiscal;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Configuracao;

public sealed record NovaTaxaIva(string Codigo, string Descricao, decimal Percentagem, string? MotivoIsencao, DateOnly VigenteDesde);

/// <summary>
/// Taxas são versionadas por vigência: uma taxa nunca muda de percentagem; uma alteração legal
/// fecha a taxa antiga (VigenteAte) e cria uma nova. Assim os documentos antigos continuam coerentes.
/// </summary>
public sealed class ServicoTaxasIva(IAppDbContext db, IServicoAuditoria auditoria)
{
    public Task<List<TaxaIva>> ListarAsync(CancellationToken ct = default) =>
        db.TaxasIva.AsNoTracking()
            .OrderBy(t => t.VigenteAte != null)
            .ThenByDescending(t => t.Percentagem)
            .ThenBy(t => t.Codigo)
            .ToListAsync(ct);

    public async Task<List<TaxaIva>> VigentesAsync(DateOnly data, CancellationToken ct = default) =>
        (await db.TaxasIva.AsNoTracking().ToListAsync(ct)).Where(t => t.EstaVigente(data)).ToList();

    public async Task<Resultado> CriarAsync(NovaTaxaIva dados, CancellationToken ct = default)
    {
        var codigo = dados.Codigo.Trim().ToUpperInvariant();
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(codigo)) erros.Add("Indique o código da taxa.");
        if (string.IsNullOrWhiteSpace(dados.Descricao)) erros.Add("Indique a descrição.");
        if (dados.Percentagem is < 0 or > 100) erros.Add("A percentagem tem de estar entre 0 e 100.");
        if (dados.Percentagem == 0 && string.IsNullOrWhiteSpace(dados.MotivoIsencao))
            erros.Add("Uma taxa de 0% precisa do motivo de isenção (menção legal impressa no documento).");

        var sobreposta = await db.TaxasIva.AnyAsync(t => t.Codigo == codigo
            && (t.VigenteAte == null || t.VigenteAte >= dados.VigenteDesde), ct);
        if (sobreposta)
            erros.Add($"Já existe uma taxa {codigo} em vigor nessa data. Feche a taxa actual antes de criar a nova.");
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        var taxa = new TaxaIva
        {
            Codigo = codigo,
            Descricao = dados.Descricao.Trim(),
            Percentagem = dados.Percentagem,
            MotivoIsencao = string.IsNullOrWhiteSpace(dados.MotivoIsencao) ? null : dados.MotivoIsencao.Trim(),
            VigenteDesde = dados.VigenteDesde,
        };
        db.TaxasIva.Add(taxa);
        await db.SaveChangesAsync(ct);
        auditoria.Registar(nameof(TaxaIva), taxa.Id.ToString(), "TaxaIva.Criar", null,
            new { taxa.Codigo, taxa.Percentagem, taxa.MotivoIsencao, taxa.VigenteDesde });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> FecharAsync(int id, DateOnly vigenteAte, CancellationToken ct = default)
    {
        var taxa = await db.TaxasIva.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (taxa is null) return Resultado.Falha("Taxa não encontrada.");
        if (taxa.VigenteAte is not null) return Resultado.Falha("Esta taxa já está fechada.");
        if (vigenteAte < taxa.VigenteDesde) return Resultado.Falha("A data de fim não pode ser anterior ao início da vigência.");

        taxa.VigenteAte = vigenteAte;
        auditoria.Registar(nameof(TaxaIva), taxa.Id.ToString(), "TaxaIva.Fechar", null, new { taxa.Codigo, vigenteAte });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }
}
