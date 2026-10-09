using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Documentos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Bezix.Facturacao.Application.Painel;

public sealed record Indicador(decimal Valor, decimal? Anterior)
{
    /// <summary>Variação face ao período anterior, em %; null quando não há base de comparação.</summary>
    public decimal? Variacao => Anterior is { } a && a != 0 ? Math.Round((Valor - a) / Math.Abs(a) * 100, 1) : null;
}

public sealed record Indicadores(
    Indicador Facturado,
    Indicador Recebido,
    Indicador Iva,
    decimal PorReceber,
    decimal Vencido,
    decimal? PrazoMedioDias,
    int FacturasEmitidas,
    int RecibosEmitidos,
    int ClientesActivos,
    int ClientesComFacturas);

public sealed record PontoSerie(string Rotulo, decimal Facturado, decimal Recebido);

public sealed record ClienteValor(int ClienteId, string Nome, decimal Valor);

public sealed record DocumentoAccao(int Id, string Numero, int ClienteId, string Cliente, DateOnly? Vencimento, decimal EmAberto, int DiasAtraso);

public enum PeriodoSerie { Diario, Mensal, Anual }

/// <summary>
/// Números do painel. Valores em meticais; só documentos emitidos (anulados não contam).
/// Os agregados ficam 5 minutos em cache, por utilizador quando a visibilidade "só os próprios" está activa.
/// </summary>
public sealed class ServicoPainel(
    IAppDbContext db,
    ServicoDocumentos documentos,
    ServicoClientes clientes,
    ServicoContaCorrente contaCorrente,
    IUtilizadorActual utilizador,
    IMemoryCache cache)
{
    private static readonly TimeSpan Duracao = TimeSpan.FromMinutes(5);
    private static readonly TipoDocumento[] Facturacao = [TipoDocumento.FT, TipoDocumento.FR, TipoDocumento.FG, TipoDocumento.ND];

    private async Task<string> ChaveAsync(string nome) =>
        $"painel:{nome}:{contaCorrente.Hoje():yyyyMMdd}:{(await clientes.SoProprioAsync() ? utilizador.Id : "todos")}";

    private async Task<T> EmCacheAsync<T>(string nome, Func<Task<T>> calcular)
    {
        var chave = await ChaveAsync(nome);
        if (cache.TryGetValue(chave, out T? valor) && valor is not null) return valor;
        valor = await calcular();
        cache.Set(chave, valor, Duracao);
        return valor;
    }

    private async Task<IQueryable<Documento>> EmitidosAsync() =>
        (await documentos.VisiveisAsync()).AsNoTracking().Where(d => d.Estado == EstadoDocumento.Emitido);

    // ------------------------------------------------------------------ Indicadores

    public Task<Indicadores> IndicadoresAsync() => EmCacheAsync("indicadores", async () =>
    {
        var hoje = contaCorrente.Hoje();
        var inicioMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var inicioAnterior = inicioMes.AddMonths(-1);
        // Mês anterior até ao mesmo dia, para a comparação ser justa a meio do mês.
        var fimAnterior = inicioAnterior.AddDays(Math.Min(hoje.Day, DateTime.DaysInMonth(inicioAnterior.Year, inicioAnterior.Month)) - 1);

        var q = await EmitidosAsync();
        var mes = await Somas(q, inicioMes, hoje);
        var anterior = await Somas(q, inicioAnterior, fimAnterior);

        var antiguidade = await contaCorrente.AntiguidadeAsync(hoje);
        var porReceber = antiguidade.Sum(l => l.AVencer + l.Vencido);
        var vencido = antiguidade.Sum(l => l.Vencido);

        // Prazo médio de recebimento: dias entre a data da factura e a do pagamento, ponderado pelo valor (últimos 12 meses).
        var desde = hoje.AddMonths(-12);
        var alocacoes = await db.PagamentoAlocacoes.AsNoTracking()
            .Where(a => a.Data >= desde && a.Pagamento!.Recibo!.Estado == EstadoDocumento.Emitido)
            .Select(a => new { a.Valor, a.Data, DataDoc = a.Documento!.Data })
            .ToListAsync();
        decimal? prazo = alocacoes.Sum(a => a.Valor) is var total && total > 0
            ? Math.Round(alocacoes.Sum(a => a.Valor * (a.Data.DayNumber - a.DataDoc.DayNumber)) / total, 0)
            : null;

        var facturas = await q.CountAsync(d => Facturacao.Contains(d.TipoDoc) && d.Data >= inicioMes && d.Data <= hoje);
        var recibos = await q.CountAsync(d => d.TipoDoc == TipoDocumento.RC && d.Data >= inicioMes && d.Data <= hoje);
        var activos = await (await clientes.VisiveisAsync()).CountAsync(c => c.Activo);
        var comFacturas = await q.Where(d => Facturacao.Contains(d.TipoDoc) && d.Data >= hoje.AddMonths(-12)).Select(d => d.ClienteId).Distinct().CountAsync();

        return new Indicadores(
            new Indicador(mes.Facturado, anterior.Facturado),
            new Indicador(mes.Recebido, anterior.Recebido),
            new Indicador(mes.Iva, anterior.Iva),
            porReceber, vencido, prazo, facturas, recibos, activos, comFacturas);
    });

    private sealed record Soma(decimal Facturado, decimal Recebido, decimal Iva);

    /// <summary>Facturado = FT+FR+FG+ND − NC; Recebido = RC + FR; IVA = IVA facturado − IVA das NC.</summary>
    private static async Task<Soma> Somas(IQueryable<Documento> q, DateOnly de, DateOnly ate)
    {
        var linhas = await q.Where(d => d.Data >= de && d.Data <= ate)
            .GroupBy(d => d.TipoDoc)
            .Select(g => new { Tipo = g.Key, Total = g.Sum(d => d.TotalMzn), Iva = g.Sum(d => d.TotalIvaMzn) })
            .ToListAsync();
        decimal Total(params TipoDocumento[] t) => linhas.Where(l => t.Contains(l.Tipo)).Sum(l => l.Total);
        decimal Iva(params TipoDocumento[] t) => linhas.Where(l => t.Contains(l.Tipo)).Sum(l => l.Iva);
        return new Soma(
            Total(Facturacao) - Total(TipoDocumento.NC),
            Total(TipoDocumento.RC, TipoDocumento.FR),
            Iva(Facturacao) - Iva(TipoDocumento.NC));
    }

    // ------------------------------------------------------------------ Gráficos

    public Task<List<PontoSerie>> SerieAsync(PeriodoSerie periodo) => EmCacheAsync($"serie:{periodo}", async () =>
    {
        var hoje = contaCorrente.Hoje();
        var q = await EmitidosAsync();
        var de = periodo switch
        {
            PeriodoSerie.Diario => hoje.AddDays(-29),
            PeriodoSerie.Mensal => new DateOnly(hoje.Year, hoje.Month, 1).AddMonths(-11),
            _ => new DateOnly(hoje.Year - 4, 1, 1),
        };
        var dados = await q.Where(d => d.Data >= de && d.Data <= hoje
                && (Facturacao.Contains(d.TipoDoc) || d.TipoDoc == TipoDocumento.NC || d.TipoDoc == TipoDocumento.RC))
            .Select(d => new { d.Data, d.TipoDoc, d.TotalMzn })
            .ToListAsync();

        string Chave(DateOnly d) => periodo switch
        {
            PeriodoSerie.Diario => d.ToString("yyyy-MM-dd"),
            PeriodoSerie.Mensal => d.ToString("yyyy-MM"),
            _ => d.Year.ToString(),
        };
        var pontos = new List<(string Chave, string Rotulo)>();
        if (periodo == PeriodoSerie.Diario)
            for (var d = de; d <= hoje; d = d.AddDays(1)) pontos.Add((Chave(d), d.ToString("dd/MM")));
        else if (periodo == PeriodoSerie.Mensal)
            for (var d = de; d <= hoje; d = d.AddMonths(1)) pontos.Add((Chave(d), Meses[d.Month - 1] + (d.Month == 1 || pontos.Count == 0 ? $" {d:yy}" : "")));
        else
            for (var a = de.Year; a <= hoje.Year; a++) pontos.Add((a.ToString(), a.ToString()));

        var porChave = dados.GroupBy(d => Chave(d.Data)).ToDictionary(g => g.Key, g => (
            Facturado: g.Where(x => Facturacao.Contains(x.TipoDoc)).Sum(x => x.TotalMzn) - g.Where(x => x.TipoDoc == TipoDocumento.NC).Sum(x => x.TotalMzn),
            Recebido: g.Where(x => x.TipoDoc is TipoDocumento.RC or TipoDocumento.FR).Sum(x => x.TotalMzn)));

        return pontos.Select(p => porChave.TryGetValue(p.Chave, out var v)
            ? new PontoSerie(p.Rotulo, v.Facturado, v.Recebido)
            : new PontoSerie(p.Rotulo, 0, 0)).ToList();
    });

    private static readonly string[] Meses = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    public Task<List<ClienteValor>> TopClientesAsync() => EmCacheAsync("topClientes", async () =>
    {
        var desde = contaCorrente.Hoje().AddMonths(-12);
        var q = await EmitidosAsync();
        var linhas = await q.Where(d => d.Data >= desde && (Facturacao.Contains(d.TipoDoc) || d.TipoDoc == TipoDocumento.NC))
            .GroupBy(d => new { d.ClienteId, d.ClienteNome })
            .Select(g => new
            {
                g.Key.ClienteId, g.Key.ClienteNome,
                Valor = g.Sum(d => d.TipoDoc == TipoDocumento.NC ? -d.TotalMzn : d.TotalMzn),
            })
            .ToListAsync();
        // O nome pode ter mudado entre documentos: agrupa por cliente e usa o nome mais recente.
        return linhas.GroupBy(l => l.ClienteId)
            .Select(g => new ClienteValor(g.Key, g.Last().ClienteNome, g.Sum(x => x.Valor)))
            .Where(c => c.Valor > 0)
            .OrderByDescending(c => c.Valor).Take(10).ToList();
    });

    public Task<List<ClienteValor>> TopDevedoresAsync() => EmCacheAsync("topDevedores", async () =>
        (await contaCorrente.AntiguidadeAsync(contaCorrente.Hoje()))
            .Where(l => l.Saldo > 0)
            .OrderByDescending(l => l.Vencido).ThenByDescending(l => l.Saldo)
            .Take(10)
            .Select(l => new ClienteValor(l.ClienteId, l.ClienteNome, l.Saldo))
            .ToList());

    public Task<List<LinhaAntiguidade>> AntiguidadeAsync() => EmCacheAsync("antiguidade", () => contaCorrente.AntiguidadeAsync(contaCorrente.Hoje()));

    // ------------------------------------------------------------------ O que fazer hoje (sem cache: tem de reflectir acções acabadas de fazer)

    /// <summary>Facturas vencidas e a vencer nos próximos 7 dias, com valor em aberto.</summary>
    public async Task<(List<DocumentoAccao> Vencidas, List<DocumentoAccao> AVencer)> CobrancasAsync(int maximo = 8)
    {
        var hoje = contaCorrente.Hoje();
        var limite = hoje.AddDays(7);
        var q = await EmitidosAsync();
        var candidatos = await q.Where(d => (d.TipoDoc == TipoDocumento.FT || d.TipoDoc == TipoDocumento.FG || d.TipoDoc == TipoDocumento.ND)
                && d.Vencimento != null && d.Vencimento <= limite)
            .OrderBy(d => d.Vencimento)
            .Select(d => new { d.Id, d.NumeroFormatado, d.ClienteId, d.ClienteNome, d.Vencimento })
            .Take(500)
            .ToListAsync();
        var situacao = await contaCorrente.SituacaoAsync(candidatos.Select(c => c.Id).ToList());
        var abertos = candidatos
            .Where(c => situacao.TryGetValue(c.Id, out var s) && s.EmAberto > 0)
            .Select(c => new DocumentoAccao(c.Id, c.NumeroFormatado!, c.ClienteId, c.ClienteNome, c.Vencimento, situacao[c.Id].EmAberto,
                Math.Max(0, hoje.DayNumber - c.Vencimento!.Value.DayNumber)))
            .ToList();
        return (
            abertos.Where(a => a.Vencimento < hoje).OrderByDescending(a => a.DiasAtraso).Take(maximo).ToList(),
            abertos.Where(a => a.Vencimento >= hoje).OrderBy(a => a.Vencimento).Take(maximo).ToList());
    }

    public async Task<List<DocumentoResumo>> RascunhosAsync(int maximo = 5)
    {
        var q = (await documentos.VisiveisAsync()).AsNoTracking().Where(d => d.Estado == EstadoDocumento.Rascunho);
        if (!utilizador.TemPermissao(Domain.Seguranca.Permissoes.Documentos.VerTodos)) q = q.Where(d => d.ResponsavelId == utilizador.Id);
        return await q.OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt).Take(maximo)
            .Select(d => new DocumentoResumo(d.Id, d.TipoDoc, d.NumeroFormatado, d.Estado, d.Data, d.Vencimento, d.ClienteId, d.ClienteNome, d.Moeda, d.Total, d.TotalMzn))
            .ToListAsync();
    }

    public Task<int> EmailsFalhadosAsync() =>
        db.EmailsFila.AsNoTracking().CountAsync(e => e.Estado == EstadoEmail.Falhou && e.CreatedAt >= DateTime.UtcNow.AddDays(-30));

    /// <summary>Séries do ano anterior ainda abertas (devem ser fechadas no fim do ano).</summary>
    public Task<List<Serie>> SeriesPorFecharAsync() =>
        db.Series.AsNoTracking().Where(s => s.Estado == EstadoSerie.Activa && s.Ano < contaCorrente.Hoje().Year).OrderBy(s => s.Ano).ToListAsync();

    public async Task<List<DocumentoResumo>> UltimosAsync(TipoDocumento[] tipos, int maximo)
    {
        var q = await EmitidosAsync();
        return await q.Where(d => tipos.Contains(d.TipoDoc))
            .OrderByDescending(d => d.EmitidoEm).Take(maximo)
            .Select(d => new DocumentoResumo(d.Id, d.TipoDoc, d.NumeroFormatado, d.Estado, d.Data, d.Vencimento, d.ClienteId, d.ClienteNome, d.Moeda, d.Total, d.TotalMzn))
            .ToListAsync();
    }

    /// <summary>Linha do tempo: últimas acções relevantes registadas na auditoria.</summary>
    public Task<List<Domain.Auditoria.RegistoAuditoria>> ActividadeRecenteAsync(int maximo = 8)
    {
        string[] accoes = ["Documento.Emitir", "Recibo.Emitir", "Documento.Anular", "Documento.PedirAprovacao", "Documento.Enviar",
            "Documento.Visto", "Pagamento.Comunicado", "Cliente.Criar"];
        return db.Auditoria.AsNoTracking().Where(a => accoes.Contains(a.Accao)).OrderByDescending(a => a.Id).Take(maximo).ToListAsync();
    }
}
