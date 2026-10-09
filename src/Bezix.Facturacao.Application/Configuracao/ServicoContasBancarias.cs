using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Configuracao;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Configuracao;

public sealed record DadosContaBancaria(
    string Banco,
    string NumeroConta,
    string? Nib,
    string? Iban,
    string? Swift,
    bool MostrarNoDocumento,
    int Ordem);

public sealed class ServicoContasBancarias(IAppDbContext db, IServicoAuditoria auditoria)
{
    public Task<List<ContaBancaria>> ListarAsync(CancellationToken ct = default) =>
        db.ContasBancarias.AsNoTracking().OrderBy(c => c.Ordem).ThenBy(c => c.Banco).ToListAsync(ct);

    public Task<ContaBancaria?> ObterAsync(int id, CancellationToken ct = default) =>
        db.ContasBancarias.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task<Resultado> GuardarAsync(int? id, DadosContaBancaria dados, CancellationToken ct = default)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(dados.Banco)) erros.Add("Indique o banco.");
        if (string.IsNullOrWhiteSpace(dados.NumeroConta)) erros.Add("Indique o número de conta.");
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        ContaBancaria conta;
        object? antes = null;
        if (id is null)
        {
            conta = new ContaBancaria();
            db.ContasBancarias.Add(conta);
        }
        else
        {
            conta = await db.ContasBancarias.SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new KeyNotFoundException("Conta bancária não encontrada.");
            antes = Instantaneo(conta);
        }

        conta.Banco = dados.Banco.Trim();
        conta.NumeroConta = dados.NumeroConta.Trim();
        conta.Nib = Limpar(dados.Nib);
        conta.Iban = Limpar(dados.Iban)?.Replace(" ", "").ToUpperInvariant();
        conta.Swift = Limpar(dados.Swift)?.ToUpperInvariant();
        conta.MostrarNoDocumento = dados.MostrarNoDocumento;
        conta.Ordem = dados.Ordem;

        await db.SaveChangesAsync(ct);
        auditoria.Registar(nameof(ContaBancaria), conta.Id.ToString(),
            id is null ? "ContaBancaria.Criar" : "ContaBancaria.Alterar", antes, Instantaneo(conta));
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public async Task RemoverAsync(int id, CancellationToken ct = default)
    {
        var conta = await db.ContasBancarias.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (conta is null) return;
        // Os documentos emitidos guardam cópia das contas no PDF, por isso remover não altera documentos antigos.
        db.ContasBancarias.Remove(conta);
        auditoria.Registar(nameof(ContaBancaria), id.ToString(), "ContaBancaria.Remover", Instantaneo(conta));
        await db.SaveChangesAsync(ct);
    }

    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static object Instantaneo(ContaBancaria c) =>
        new { c.Banco, c.NumeroConta, c.Nib, c.Iban, c.Swift, c.MostrarNoDocumento, c.Ordem };
}
