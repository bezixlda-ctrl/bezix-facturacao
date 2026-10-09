using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Recebimentos;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bezix.Facturacao.Application.Recebimentos;

public sealed record Alocacao(int DocumentoId, decimal Valor);

public sealed record DadosRecibo(
    int ClienteId,
    DateOnly Data,
    decimal Valor,
    MetodoPagamento Metodo,
    string? Referencia,
    string? Observacoes,
    IReadOnlyList<Alocacao> Alocacoes);

public sealed record AlocacaoVista(int DocumentoId, string NumeroFormatado, DateOnly DataDocumento, decimal TotalMzn, decimal Valor, DateOnly Data);

public sealed record PagamentoVista(int Id, int ReciboId, decimal Valor, decimal Alocado, IReadOnlyList<AlocacaoVista> Alocacoes)
{
    public decimal PorAlocar => Valor - Alocado;
}

/// <summary>
/// Recibos (RC): registam um pagamento em meticais e alocam-no a uma ou várias facturas/notas de débito.
/// O que sobra fica como adiantamento do cliente, que se pode alocar mais tarde a outras facturas.
/// Corre numa transacção com a série RC bloqueada: dois recibos ao mesmo tempo não pagam duas vezes a mesma factura.
/// </summary>
public sealed class ServicoRecibos(
    IAppDbContext db,
    ITransacoes transacoes,
    IBloqueioSerie bloqueio,
    NumeradorDocumentos numerador,
    ServicoContaCorrente contaCorrente,
    ServicoClientes clientes,
    ServicoEmpresa empresas,
    ServicoPdfDocumentos pdfs,
    IUtilizadorActual utilizador,
    IServicoAuditoria auditoria,
    ILogger<ServicoRecibos> log)
{
    public async Task<(Resultado Resultado, int? ReciboId)> RegistarAsync(DadosRecibo d, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Recibos.Registar)) return (Resultado.Falha("O seu perfil não permite registar recibos."), null);

        var erros = Validar(d, contaCorrente.Hoje());
        var cliente = await clientes.ObterAsync(d.ClienteId, ct);
        if (cliente is null) erros.Add("Escolha o cliente.");
        var empresa = await empresas.ObterAsync(ct);
        if (empresa.Nuit == ServicoEmissao.NuitEmpresaPorPreencher) erros.Add("Preencha o NUIT da empresa em Configurações antes de emitir recibos.");
        var serieId = await db.Series.AsNoTracking()
            .Where(s => s.TipoDoc == TipoDocumento.RC && s.Ano == d.Data.Year && s.Estado == EstadoSerie.Activa)
            .OrderBy(s => s.Codigo).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        if (serieId is null) erros.Add($"Não há série de recibos activa para {d.Data.Year}. Abra uma em Configurações → Séries.");
        if (erros.Count > 0) return (Resultado.Falha([.. erros]), null);

        var resultado = await transacoes.ExecutarAsync<(Resultado, int?)>(async c =>
        {
            var serie = await bloqueio.BloquearAsync(serieId!.Value, c);
            if (serie.UltimaData is { } ultima && d.Data < ultima)
                return (Resultado.Falha($"A data não pode ser anterior à do último recibo da série ({ultima:dd/MM/yyyy})."), null);

            var errosAlocacao = await ValidarAlocacoesAsync(d.ClienteId, d.Alocacoes, c);
            if (errosAlocacao.Count > 0) return (Resultado.Falha([.. errosAlocacao]), null);

            var contas = await db.ContasBancarias.AsNoTracking().ToListAsync(c);
            var recibo = new Documento
            {
                TipoDoc = TipoDocumento.RC,
                Data = d.Data,
                ClienteId = cliente!.Id,
                EmitenteJson = DadosEmitente.De(empresa, contas).ParaJson(),
                Moeda = Moeda.Base,
                Cambio = 1,
                Subtotal = d.Valor,
                Total = d.Valor,
                TotalMzn = d.Valor,
                MetodoPagamento = d.Metodo,
                ReferenciaPagamento = Limpar(d.Referencia),
                Observacoes = Limpar(d.Observacoes),
                ResponsavelId = utilizador.Id,
            };
            ServicoDocumentos.CopiarCliente(recibo, cliente);
            await numerador.NumerarAsync(recibo, serie, c);
            db.Documentos.Add(recibo);

            var pagamento = new Pagamento
            {
                ClienteId = cliente.Id,
                Data = d.Data,
                Valor = d.Valor,
                Metodo = d.Metodo,
                Referencia = recibo.ReferenciaPagamento,
                Observacoes = recibo.Observacoes,
                Recibo = recibo,
            };
            foreach (var a in d.Alocacoes.Where(a => a.Valor > 0))
                pagamento.Alocacoes.Add(new PagamentoAlocacao { DocumentoId = a.DocumentoId, Valor = a.Valor, Data = d.Data });
            db.Pagamentos.Add(pagamento);
            await db.SaveChangesAsync(c);

            auditoria.Registar(nameof(Documento), recibo.Id.ToString(), "Recibo.Emitir", null, new
            {
                recibo.NumeroFormatado, cliente.Nome, d.Valor, Metodo = d.Metodo.ToString(), d.Referencia,
                Alocacoes = d.Alocacoes.Select(a => new { a.DocumentoId, a.Valor }),
                Adiantamento = d.Valor - d.Alocacoes.Sum(a => a.Valor),
            });
            await db.SaveChangesAsync(c);
            return (Resultado.Ok(), recibo.Id);
        }, ct);

        if (resultado.Item2 is { } id)
        {
            try { await pdfs.GarantirPdfGuardadoAsync(id, ct); }
            catch (Exception ex) { log.LogError(ex, "Falha a gerar o PDF do recibo {Id}.", id); }
        }
        return resultado;
    }

    /// <summary>Aplica parte de um adiantamento (pagamento ainda não alocado) a facturas em aberto do mesmo cliente.</summary>
    public async Task<Resultado> AlocarAdiantamentoAsync(int pagamentoId, IReadOnlyList<Alocacao> alocacoes, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Recibos.Registar)) return Resultado.Falha("O seu perfil não permite alocar pagamentos.");
        var info = await db.Pagamentos.AsNoTracking().Where(p => p.Id == pagamentoId)
            .Select(p => new { p.ClienteId, p.Recibo!.SerieId, p.Recibo.Estado }).SingleOrDefaultAsync(ct);
        if (info is null || await clientes.ObterAsync(info.ClienteId, ct) is null) return Resultado.Falha("Pagamento não encontrado.");
        if (info.Estado != EstadoDocumento.Emitido) return Resultado.Falha("O recibo deste pagamento está anulado.");
        var lista = alocacoes.Where(a => a.Valor > 0).ToList();
        if (lista.Count == 0) return Resultado.Falha("Indique quanto alocar a cada factura.");

        return await transacoes.ExecutarAsync(async c =>
        {
            await bloqueio.BloquearAsync(info.SerieId, c);
            var pagamento = await db.Pagamentos.Include(p => p.Alocacoes).SingleAsync(p => p.Id == pagamentoId, c);
            var porAlocar = pagamento.Valor - pagamento.Alocacoes.Sum(a => a.Valor);
            var total = lista.Sum(a => a.Valor);
            if (total > porAlocar) return Resultado.Falha($"Só há {porAlocar:#,##0.00} MT por alocar neste pagamento.");

            var erros = await ValidarAlocacoesAsync(pagamento.ClienteId, lista, c);
            if (erros.Count > 0) return Resultado.Falha([.. erros]);

            var hoje = contaCorrente.Hoje();
            foreach (var a in lista)
                pagamento.Alocacoes.Add(new PagamentoAlocacao { DocumentoId = a.DocumentoId, Valor = a.Valor, Data = hoje });
            auditoria.Registar(nameof(Pagamento), pagamentoId.ToString(), "Pagamento.AlocarAdiantamento", null,
                new { Alocacoes = lista.Select(a => new { a.DocumentoId, a.Valor }) });
            await db.SaveChangesAsync(c);
            return Resultado.Ok();
        }, ct);
    }

    public async Task<PagamentoVista?> PagamentoDoReciboAsync(int reciboId, CancellationToken ct = default)
    {
        var p = await db.Pagamentos.AsNoTracking()
            .Where(x => x.ReciboId == reciboId)
            .Select(x => new
            {
                x.Id, x.ReciboId, x.Valor,
                Alocacoes = x.Alocacoes.OrderBy(a => a.Id).Select(a => new AlocacaoVista(
                    a.DocumentoId, a.Documento!.NumeroFormatado!, a.Documento.Data, a.Documento.TotalMzn, a.Valor, a.Data)).ToList(),
            })
            .SingleOrDefaultAsync(ct);
        return p is null ? null : new PagamentoVista(p.Id, p.ReciboId, p.Valor, p.Alocacoes.Sum(a => a.Valor), p.Alocacoes);
    }

    /// <summary>Pagamentos do cliente com valor ainda por alocar (adiantamentos).</summary>
    public async Task<List<PagamentoVista>> AdiantamentosAsync(int clienteId, CancellationToken ct = default)
    {
        var ids = await db.Pagamentos.AsNoTracking()
            .Where(p => p.ClienteId == clienteId && p.Recibo!.Estado == EstadoDocumento.Emitido
                && p.Valor > (p.Alocacoes.Sum(a => (decimal?)a.Valor) ?? 0))
            .OrderBy(p => p.Data).Select(p => p.ReciboId).ToListAsync(ct);
        var lista = new List<PagamentoVista>();
        foreach (var id in ids) lista.Add((await PagamentoDoReciboAsync(id, ct))!);
        return lista;
    }

    /// <summary>Recibos onde este documento foi pago (para a página do documento).</summary>
    public async Task<List<(int ReciboId, string Numero, DateOnly Data, decimal Valor)>> PagamentosDoDocumentoAsync(int documentoId, CancellationToken ct = default)
    {
        var lista = await db.PagamentoAlocacoes.AsNoTracking()
            .Where(a => a.DocumentoId == documentoId && a.Pagamento!.Recibo!.Estado == EstadoDocumento.Emitido)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Pagamento!.ReciboId, a.Pagamento.Recibo!.NumeroFormatado, a.Data, a.Valor })
            .ToListAsync(ct);
        return lista.Select(x => (x.ReciboId, x.NumeroFormatado!, x.Data, x.Valor)).ToList();
    }

    // ------------------------------------------------------------------ Regras

    public static List<string> Validar(DadosRecibo d, DateOnly hoje)
    {
        var erros = new List<string>();
        if (d.Valor <= 0) erros.Add("Indique o valor recebido.");
        if (decimal.Round(d.Valor, 2) != d.Valor) erros.Add("O valor só pode ter 2 casas decimais.");
        if (d.Data > hoje) erros.Add("A data do recibo não pode ser futura.");
        if (d.Alocacoes.Any(a => a.Valor < 0 || decimal.Round(a.Valor, 2) != a.Valor)) erros.Add("Os valores a alocar têm de ser positivos e ter no máximo 2 casas decimais.");
        if (d.Alocacoes.Where(a => a.Valor > 0).GroupBy(a => a.DocumentoId).Any(g => g.Count() > 1)) erros.Add("Cada documento só pode aparecer uma vez.");
        if (d.Alocacoes.Sum(a => a.Valor) > d.Valor) erros.Add("A soma alocada às facturas passa o valor recebido.");
        return erros;
    }

    private async Task<List<string>> ValidarAlocacoesAsync(int clienteId, IReadOnlyList<Alocacao> alocacoes, CancellationToken ct)
    {
        var erros = new List<string>();
        var lista = alocacoes.Where(a => a.Valor > 0).ToList();
        if (lista.Count == 0) return erros;
        var abertos = (await contaCorrente.EmAbertoAsync(clienteId, ct)).ToDictionary(a => a.Id);
        foreach (var a in lista)
        {
            if (!abertos.TryGetValue(a.DocumentoId, out var doc))
                erros.Add("Um dos documentos já não está em aberto (foi pago, creditado ou anulado entretanto). Recarregue a página.");
            else if (a.Valor > doc.EmAberto)
                erros.Add($"{doc.NumeroFormatado}: só faltam {doc.EmAberto:#,##0.00} MT.");
        }
        return erros.Distinct().ToList();
    }

    private static string? Limpar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
