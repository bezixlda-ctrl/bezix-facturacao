using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Recebimentos;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Recebimentos;

/// <summary>Situação de pagamento de um documento a débito (valores em MZN).</summary>
public sealed record SituacaoDocumento(decimal TotalMzn, decimal Pago, decimal Creditado, decimal EmAberto, EstadoPagamento Estado, int DiasAtraso);

public sealed record DocumentoAberto(
    int Id, TipoDocumento Tipo, string NumeroFormatado, DateOnly Data, DateOnly? Vencimento,
    string Moeda, decimal Total, decimal TotalMzn, decimal EmAberto, int DiasAtraso);

public sealed record SaldoCliente(decimal Debitos, decimal Creditos, decimal Saldo, decimal Vencido, decimal AdiantamentoPorAlocar);

public sealed record MovimentoExtracto(DateOnly Data, int DocumentoId, string Documento, string Descricao, decimal Debito, decimal Credito, decimal Saldo);

public sealed record Extracto(int ClienteId, string ClienteNome, string? ClienteNuit, DateOnly De, DateOnly Ate,
    decimal SaldoInicial, IReadOnlyList<MovimentoExtracto> Movimentos, decimal TotalDebitos, decimal TotalCreditos, decimal SaldoFinal);

public sealed record LinhaAntiguidade(int ClienteId, string ClienteNome, decimal AVencer, decimal Ate30, decimal De31a60, decimal De61a90, decimal Mais90, decimal Creditos, decimal Saldo)
{
    public decimal Vencido => Ate30 + De31a60 + De61a90 + Mais90;
}

public sealed record ClienteSaldoLinha(int ClienteId, string Nome, string? Nuit, decimal Saldo, decimal Vencido, DateOnly? VencimentoMaisAntigo);

/// <summary>
/// Conta corrente em meticais, calculada a partir dos documentos emitidos (os anulados não contam):
/// débitos FT, FG, ND e FR; créditos NC, RC e FR (a factura-recibo liquida-se a si própria).
/// Facturas em moeda estrangeira entram pelo contravalor em MZN gravado na emissão.
/// </summary>
public sealed class ServicoContaCorrente(IAppDbContext db, ServicoClientes clientes, TimeProvider relogio)
{
    private static readonly TipoDocumento[] TiposDebito = [TipoDocumento.FT, TipoDocumento.FG, TipoDocumento.ND, TipoDocumento.FR];
    private static readonly TipoDocumento[] TiposAPagar = [TipoDocumento.FT, TipoDocumento.FG, TipoDocumento.ND];

    public DateOnly Hoje() => DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime.AddHours(2));

    // ------------------------------------------------------------------ Por documento

    public async Task<Dictionary<int, SituacaoDocumento>> SituacaoAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        var docs = await db.Documentos.AsNoTracking()
            .Where(d => ids.Contains(d.Id) && d.Estado == EstadoDocumento.Emitido && TiposDebito.Contains(d.TipoDoc))
            .Select(d => new { d.Id, d.TipoDoc, d.TotalMzn, d.Data, d.Vencimento })
            .ToListAsync(ct);
        var idsDebito = docs.Select(d => d.Id).ToList();
        var pagos = await PagoPorDocumentoAsync(idsDebito, ct);
        var creditados = await CreditadoPorDocumentoAsync(idsDebito, ct);
        var hoje = Hoje();

        return docs.ToDictionary(d => d.Id, d => Situacao(d.TipoDoc, d.TotalMzn, d.Data, d.Vencimento,
            pagos.GetValueOrDefault(d.Id), creditados.GetValueOrDefault(d.Id), hoje));
    }

    public static SituacaoDocumento Situacao(TipoDocumento tipo, decimal totalMzn, DateOnly data, DateOnly? vencimento, decimal pago, decimal creditado, DateOnly hoje)
    {
        if (tipo == TipoDocumento.FR) return new(totalMzn, totalMzn, 0, 0, EstadoPagamento.Pago, 0);
        var aberto = Math.Max(0, totalMzn - pago - creditado);
        var atraso = Math.Max(0, hoje.DayNumber - (vencimento ?? data).DayNumber);
        var estado = aberto == 0 ? EstadoPagamento.Pago
            : vencimento is { } v && v < hoje ? EstadoPagamento.Vencido
            : aberto < totalMzn ? EstadoPagamento.Parcial
            : EstadoPagamento.PorPagar;
        return new(totalMzn, pago, creditado, aberto, estado, aberto > 0 ? atraso : 0);
    }

    /// <summary>Facturas e notas de débito do cliente com valor em aberto, da mais antiga para a mais recente.</summary>
    public async Task<List<DocumentoAberto>> EmAbertoAsync(int clienteId, CancellationToken ct = default)
    {
        var docs = await db.Documentos.AsNoTracking()
            .Where(d => d.ClienteId == clienteId && d.Estado == EstadoDocumento.Emitido && TiposAPagar.Contains(d.TipoDoc))
            .Select(d => new { d.Id, d.TipoDoc, d.NumeroFormatado, d.Data, d.Vencimento, d.Moeda, d.Total, d.TotalMzn })
            .ToListAsync(ct);
        var ids = docs.Select(d => d.Id).ToList();
        var pagos = await PagoPorDocumentoAsync(ids, ct);
        var creditados = await CreditadoPorDocumentoAsync(ids, ct);
        var hoje = Hoje();

        return docs
            .Select(d => (d, s: Situacao(d.TipoDoc, d.TotalMzn, d.Data, d.Vencimento, pagos.GetValueOrDefault(d.Id), creditados.GetValueOrDefault(d.Id), hoje)))
            .Where(x => x.s.EmAberto > 0)
            .OrderBy(x => x.d.Vencimento ?? x.d.Data).ThenBy(x => x.d.Data).ThenBy(x => x.d.Id)
            .Select(x => new DocumentoAberto(x.d.Id, x.d.TipoDoc, x.d.NumeroFormatado!, x.d.Data, x.d.Vencimento, x.d.Moeda, x.d.Total, x.d.TotalMzn, x.s.EmAberto, x.s.DiasAtraso))
            .ToList();
    }

    // ------------------------------------------------------------------ Por cliente

    public async Task<SaldoCliente> SaldoAsync(int clienteId, CancellationToken ct = default)
    {
        var movimentos = await MovimentosAsync(clienteId, null, null, ct);
        var debitos = movimentos.Sum(m => m.Debito);
        var creditos = movimentos.Sum(m => m.Credito);
        var abertos = await EmAbertoAsync(clienteId, ct);
        var hoje = Hoje();
        var vencido = abertos.Where(a => a.Vencimento is { } v && v < hoje).Sum(a => a.EmAberto);
        return new SaldoCliente(debitos, creditos, debitos - creditos, vencido, await AdiantamentoAsync(clienteId, ct));
    }

    /// <summary>Parte dos pagamentos ainda não aplicada a facturas (crédito do cliente).</summary>
    public async Task<decimal> AdiantamentoAsync(int clienteId, CancellationToken ct = default)
    {
        var pagamentos = await db.Pagamentos.AsNoTracking()
            .Where(p => p.ClienteId == clienteId && p.Recibo!.Estado == EstadoDocumento.Emitido)
            .Select(p => new { p.Valor, Alocado = p.Alocacoes.Sum(a => (decimal?)a.Valor) ?? 0 })
            .ToListAsync(ct);
        return pagamentos.Sum(p => p.Valor - p.Alocado);
    }

    public async Task<Extracto?> ExtractoAsync(int clienteId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var cliente = await clientes.ObterAsync(clienteId, ct);
        if (cliente is null) return null;

        var anteriores = await MovimentosAsync(clienteId, null, de.AddDays(-1), ct);
        var saldoInicial = anteriores.Sum(m => m.Debito - m.Credito);
        var periodo = await MovimentosAsync(clienteId, de, ate, ct);

        var saldo = saldoInicial;
        var linhas = new List<MovimentoExtracto>();
        foreach (var m in periodo)
        {
            saldo += m.Debito - m.Credito;
            linhas.Add(m with { Saldo = saldo });
        }
        return new Extracto(clienteId, cliente.Nome, cliente.Nuit, de, ate, saldoInicial, linhas,
            periodo.Sum(m => m.Debito), periodo.Sum(m => m.Credito), saldo);
    }

    // ------------------------------------------------------------------ Todos os clientes

    public async Task<Pagina<ClienteSaldoLinha>> SaldosAsync(string? texto, bool soComSaldo, bool soVencidos, int pagina, CancellationToken ct = default)
    {
        var (numero, tamanho) = Pagina<ClienteSaldoLinha>.Normalizar(pagina, Pagina<ClienteSaldoLinha>.TamanhoPadrao);
        var linhas = await CalcularPorClienteAsync(texto, Hoje(), ct);
        var filtradas = linhas
            .Where(l => !soComSaldo || l.Saldo != 0)
            .Where(l => !soVencidos || l.Vencido > 0)
            .OrderByDescending(l => l.Vencido).ThenByDescending(l => l.Saldo).ThenBy(l => l.ClienteNome)
            .ToList();
        var itens = filtradas.Skip((numero - 1) * tamanho).Take(tamanho)
            .Select(l => new ClienteSaldoLinha(l.ClienteId, l.ClienteNome, l.Nuit, l.Saldo, l.Vencido, l.VencimentoMaisAntigo))
            .ToList();
        return new Pagina<ClienteSaldoLinha>(itens, filtradas.Count, numero, tamanho);
    }

    /// <summary>Antiguidade de saldos por dias de atraso face ao vencimento (ou à data, se não tiver vencimento).</summary>
    public async Task<List<LinhaAntiguidade>> AntiguidadeAsync(DateOnly referencia, string? texto = null, CancellationToken ct = default) =>
        (await CalcularPorClienteAsync(texto, referencia, ct))
            .Where(l => l.Saldo != 0 || l.Linha.Vencido != 0)
            .Select(l => l.Linha)
            .OrderByDescending(l => l.Mais90).ThenByDescending(l => l.Vencido).ThenBy(l => l.ClienteNome)
            .ToList();

    private sealed record Calculo(int ClienteId, string ClienteNome, string? Nuit, decimal Saldo, decimal Vencido, DateOnly? VencimentoMaisAntigo, LinhaAntiguidade Linha);

    private async Task<List<Calculo>> CalcularPorClienteAsync(string? texto, DateOnly referencia, CancellationToken ct)
    {
        var q = (await clientes.VisiveisAsync(ct)).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(texto))
        {
            var t = texto.Trim();
            q = q.Where(c => c.Nome.Contains(t) || (c.Nuit != null && c.Nuit.Contains(t)));
        }
        var listaClientes = await q.Select(c => new { c.Id, c.Nome, c.Nuit }).ToListAsync(ct);
        var idsClientes = listaClientes.Select(c => c.Id).ToList();

        // Documentos emitidos dos clientes visíveis (só as colunas necessárias, para poupar memória).
        var docs = await db.Documentos.AsNoTracking()
            .Where(d => idsClientes.Contains(d.ClienteId) && d.Estado == EstadoDocumento.Emitido
                && (TiposDebito.Contains(d.TipoDoc) || d.TipoDoc == TipoDocumento.NC || d.TipoDoc == TipoDocumento.RC))
            .Select(d => new { d.Id, d.ClienteId, d.TipoDoc, d.Data, d.Vencimento, d.TotalMzn, d.Total })
            .ToListAsync(ct);
        var idsAPagar = docs.Where(d => TiposAPagar.Contains(d.TipoDoc)).Select(d => d.Id).ToList();
        var pagos = await PagoPorDocumentoAsync(idsAPagar, ct);
        var creditados = await CreditadoPorDocumentoAsync(idsAPagar, ct);

        var resultado = new List<Calculo>();
        foreach (var grupo in docs.GroupBy(d => d.ClienteId))
        {
            var c = listaClientes.First(x => x.Id == grupo.Key);
            decimal debitos = 0, creditos = 0, aVencer = 0, b30 = 0, b60 = 0, b90 = 0, bMais = 0;
            DateOnly? maisAntigo = null;
            foreach (var d in grupo)
            {
                if (TiposDebito.Contains(d.TipoDoc)) debitos += d.TotalMzn;
                if (d.TipoDoc is TipoDocumento.NC or TipoDocumento.FR) creditos += d.TotalMzn;
                if (d.TipoDoc == TipoDocumento.RC) creditos += d.Total;
                if (!TiposAPagar.Contains(d.TipoDoc)) continue;

                var aberto = Math.Max(0, d.TotalMzn - pagos.GetValueOrDefault(d.Id) - creditados.GetValueOrDefault(d.Id));
                if (aberto == 0) continue;
                var limite = d.Vencimento ?? d.Data;
                var atraso = referencia.DayNumber - limite.DayNumber;
                if (atraso <= 0) aVencer += aberto;
                else
                {
                    if (maisAntigo is null || limite < maisAntigo) maisAntigo = limite;
                    if (atraso <= 30) b30 += aberto;
                    else if (atraso <= 60) b60 += aberto;
                    else if (atraso <= 90) b90 += aberto;
                    else bMais += aberto;
                }
            }
            var saldo = debitos - creditos;
            var abertoTotal = aVencer + b30 + b60 + b90 + bMais;
            var linha = new LinhaAntiguidade(c.Id, c.Nome, aVencer, b30, b60, b90, bMais, saldo - abertoTotal, saldo);
            resultado.Add(new Calculo(c.Id, c.Nome, c.Nuit, saldo, linha.Vencido, maisAntigo, linha));
        }
        return resultado;
    }

    // ------------------------------------------------------------------ Auxiliares

    private async Task<List<MovimentoExtracto>> MovimentosAsync(int clienteId, DateOnly? de, DateOnly? ate, CancellationToken ct)
    {
        var q = db.Documentos.AsNoTracking()
            .Where(d => d.ClienteId == clienteId && d.Estado == EstadoDocumento.Emitido
                && (TiposDebito.Contains(d.TipoDoc) || d.TipoDoc == TipoDocumento.NC || d.TipoDoc == TipoDocumento.RC));
        if (de is { } inicio) q = q.Where(d => d.Data >= inicio);
        if (ate is { } fim) q = q.Where(d => d.Data <= fim);

        var docs = await q.OrderBy(d => d.Data).ThenBy(d => d.EmitidoEm).ThenBy(d => d.Id)
            .Select(d => new { d.Id, d.TipoDoc, d.NumeroFormatado, d.Data, d.TotalMzn, d.Total, d.Moeda, d.Vencimento, Origem = d.DocOrigem!.NumeroFormatado })
            .ToListAsync(ct);

        return docs.Select(d =>
        {
            var (debito, credito) = d.TipoDoc switch
            {
                TipoDocumento.FR => (d.TotalMzn, d.TotalMzn),
                TipoDocumento.NC => (0m, d.TotalMzn),
                TipoDocumento.RC => (0m, d.Total),
                _ => (d.TotalMzn, 0m),
            };
            var descricao = d.TipoDoc switch
            {
                TipoDocumento.NC or TipoDocumento.ND => $"{TipoDocumentoInfo.Nome(d.TipoDoc)} s/ {d.Origem}",
                TipoDocumento.FR => "Factura-recibo (paga no acto)",
                TipoDocumento.RC => "Pagamento recebido",
                _ => TipoDocumentoInfo.Nome(d.TipoDoc) + (d.Vencimento is { } v ? $", vence {v:dd/MM/yyyy}" : ""),
            };
            if (d.Moeda != Domain.Configuracao.Moeda.Base) descricao += $" ({d.Total:#,##0.00} {d.Moeda})";
            return new MovimentoExtracto(d.Data, d.Id, d.NumeroFormatado!, descricao, debito, credito, 0);
        }).ToList();
    }

    /// <summary>Soma das alocações de recibos não anulados, por documento.</summary>
    private async Task<Dictionary<int, decimal>> PagoPorDocumentoAsync(List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        return await db.PagamentoAlocacoes.AsNoTracking()
            .Where(a => ids.Contains(a.DocumentoId) && a.Pagamento!.Recibo!.Estado == EstadoDocumento.Emitido)
            .GroupBy(a => a.DocumentoId)
            .Select(g => new { g.Key, Valor = g.Sum(a => a.Valor) })
            .ToDictionaryAsync(x => x.Key, x => x.Valor, ct);
    }

    /// <summary>Soma das NC emitidas, em MZN, por documento corrigido.</summary>
    private async Task<Dictionary<int, decimal>> CreditadoPorDocumentoAsync(List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        return await db.Documentos.AsNoTracking()
            .Where(d => d.TipoDoc == TipoDocumento.NC && d.Estado == EstadoDocumento.Emitido && d.DocOrigemId != null && ids.Contains(d.DocOrigemId.Value))
            .GroupBy(d => d.DocOrigemId!.Value)
            .Select(g => new { g.Key, Valor = g.Sum(d => d.TotalMzn) })
            .ToDictionaryAsync(x => x.Key, x => x.Valor, ct);
    }
}
