using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bezix.Facturacao.Application.Documentos;

/// <summary>
/// Emissão (atribuição de número, hash, imutabilidade), aprovações e anulação.
/// A emissão corre numa transacção com a série bloqueada: dois utilizadores na mesma série nunca obtêm o mesmo número nem deixam saltos.
/// </summary>
public sealed class ServicoEmissao(
    IAppDbContext db,
    ITransacoes transacoes,
    IBloqueioSerie bloqueio,
    IUtilizadorActual utilizador,
    ServicoDocumentos documentos,
    ServicoEmpresa empresas,
    ServicoPdfDocumentos pdfs,
    IServicoAuditoria auditoria,
    TimeProvider relogio,
    ILogger<ServicoEmissao> log)
{
    public const string NuitEmpresaPorPreencher = "000000000";

    /// <summary>
    /// Emite o rascunho, ou envia-o para aprovação quando o desconto passa o limite do perfil
    /// ou quando quem pede uma NC não a pode emitir sozinho.
    /// </summary>
    public async Task<ResultadoEmissao> EmitirAsync(int id, string? justificacao = null, CancellationToken ct = default)
    {
        var doc = await documentos.ObterAsync(id, ct);
        if (doc is null) return ResultadoEmissao.Recusado("Documento não encontrado.");
        if (doc.Estado != EstadoDocumento.Rascunho) return ResultadoEmissao.Recusado("Este documento já não é um rascunho.");

        var motivosAprovacao = new List<TipoAprovacao>();
        if (!documentos.PodeEmitir(doc.TipoDoc))
        {
            if (doc.TipoDoc == TipoDocumento.NC && utilizador.TemPermissao(Permissoes.Documentos.PedirNotaCredito))
                motivosAprovacao.Add(TipoAprovacao.NotaCredito);
            else
                return ResultadoEmissao.Recusado($"O seu perfil não permite emitir {TipoDocumentoInfo.Nome(doc.TipoDoc).ToLowerInvariant()}s.");
        }

        // Validação antecipada (sem bloqueio) para não pedir aprovação de um documento que não pode ser emitido.
        var calculo = await documentos.CalcularAsync(ServicoDocumentos.ParaRascunho(doc), ct);
        var serie = await db.Series.AsNoTracking().SingleAsync(s => s.Id == doc.SerieId, ct);
        var erros = await ValidarAsync(doc, serie, calculo, ct);
        if (erros.Count > 0) return ResultadoEmissao.Recusado([.. erros]);

        if (utilizador.DescontoMaximoPct is { } limite && calculo.DescontoEfectivoPct > limite
            && !utilizador.TemPermissao(Permissoes.Documentos.DescontoSemLimite))
            motivosAprovacao.Add(TipoAprovacao.Desconto);

        if (motivosAprovacao.Count > 0)
            return await PedirAprovacaoAsync(id, motivosAprovacao, calculo.DescontoEfectivoPct, justificacao, ct);

        return await EmitirInternoAsync(id, ct);
    }

    private async Task<ResultadoEmissao> PedirAprovacaoAsync(int id, List<TipoAprovacao> motivos, decimal descontoPct, string? justificacao, CancellationToken ct)
    {
        var doc = await db.Documentos.SingleAsync(d => d.Id == id, ct);
        doc.Estado = EstadoDocumento.AguardaAprovacao;
        foreach (var m in motivos)
        {
            db.Aprovacoes.Add(new Aprovacao
            {
                DocumentoId = id,
                Tipo = m,
                PedidoPorId = utilizador.Id ?? "",
                PedidoPorNome = utilizador.Nome ?? utilizador.Email ?? "",
                Justificacao = string.IsNullOrWhiteSpace(justificacao) ? null : justificacao.Trim(),
            });
        }
        auditoria.Registar(nameof(Documento), id.ToString(), "Documento.PedirAprovacao", null,
            new { Motivos = motivos.Select(m => m.ToString()), DescontoEfectivoPct = descontoPct });
        await db.SaveChangesAsync(ct);

        var texto = motivos.Contains(TipoAprovacao.Desconto)
            ? $"O desconto ({descontoPct:0.##}%) passa o limite do seu perfil ({utilizador.DescontoMaximoPct:0.##}%)."
            : "As notas de crédito pedidas pelo seu perfil precisam de aprovação.";
        return new ResultadoEmissao(ResultadoEmissaoTipo.AguardaAprovacao, Resultado.Falha(texto + " O documento ficou a aguardar aprovação de um gestor."));
    }

    private async Task<ResultadoEmissao> EmitirInternoAsync(int id, CancellationToken ct)
    {
        var resultado = await transacoes.ExecutarAsync(async c =>
        {
            var doc = await db.Documentos.Include(d => d.Linhas).Include(d => d.IvaResumo).Include(d => d.Origens)
                .SingleAsync(d => d.Id == id, c);
            if (doc.Estado is not (EstadoDocumento.Rascunho or EstadoDocumento.AguardaAprovacao))
                return ResultadoEmissao.Recusado("Este documento já foi emitido.");

            // Bloqueio da série até ao fim da transacção: a partir daqui ninguém mais numera nesta série.
            var serie = await bloqueio.BloquearAsync(doc.SerieId, c);

            // Recálculo definitivo no servidor, com as taxas em vigor na data do documento.
            var calculo = await documentos.CalcularAsync(ServicoDocumentos.ParaRascunho(doc), c);
            var erros = await ValidarAsync(doc, serie, calculo, c);
            if (erros.Count > 0) return ResultadoEmissao.Recusado([.. erros]);

            var cliente = await db.Clientes.AsNoTracking().SingleAsync(x => x.Id == doc.ClienteId, c);
            var empresa = await empresas.ObterAsync(c);
            var contas = await db.ContasBancarias.AsNoTracking().ToListAsync(c);

            // 1.º gravar linhas e totais definitivos ainda como rascunho (os triggers bloqueiam linhas de documentos emitidos).
            documentos.AplicarCalculo(doc, calculo);
            ServicoDocumentos.CopiarCliente(doc, cliente);
            doc.EmitenteJson = DadosEmitente.De(empresa, contas).ParaJson();
            await db.SaveChangesAsync(c);

            // 2.º numerar e encadear.
            var hashAnterior = await db.Documentos.AsNoTracking()
                .Where(d => d.SerieId == serie.Id && d.Numero != null)
                .OrderByDescending(d => d.Numero)
                .Select(d => d.Hash)
                .FirstOrDefaultAsync(c);

            doc.Numero = serie.ProximoNumero;
            doc.NumeroFormatado = Serie.FormatarNumero(serie.TipoDoc, serie.Codigo, serie.Ano, serie.ProximoNumero);
            doc.EmitidoEm = TruncarSegundos(relogio.GetUtcNow().UtcDateTime);
            doc.EmitidoPor = utilizador.Email ?? "sistema";
            doc.HashAnterior = hashAnterior;
            doc.Hash = HashDocumento.Calcular(doc);
            doc.Estado = EstadoDocumento.Emitido;
            serie.ProximoNumero++;
            serie.UltimaData = doc.Data;

            var pendentes = await db.Aprovacoes.Where(a => a.DocumentoId == id && a.Estado == EstadoAprovacao.Pendente).ToListAsync(c);
            foreach (var a in pendentes)
            {
                a.Estado = EstadoAprovacao.Aprovada;
                a.DecididoPorNome = utilizador.Nome ?? utilizador.Email;
                a.DecididoEm = doc.EmitidoEm;
            }

            auditoria.Registar(nameof(Documento), doc.Id.ToString(), "Documento.Emitir", null, new
            {
                doc.NumeroFormatado, Tipo = doc.TipoDoc.ToString(), doc.ClienteNome, doc.ClienteNuit, doc.Moeda, doc.Cambio,
                doc.Total, doc.TotalMzn, doc.Hash, Aprovado = pendentes.Count > 0,
            });
            await db.SaveChangesAsync(c);
            return new ResultadoEmissao(ResultadoEmissaoTipo.Emitido, Resultado.Ok(), doc.NumeroFormatado);
        }, ct);

        if (resultado.Tipo == ResultadoEmissaoTipo.Emitido)
        {
            // O PDF gera-se depois do commit, para a série não ficar bloqueada durante a geração.
            // Se falhar, é gerado (idêntico) no primeiro pedido de impressão.
            try { await pdfs.GarantirPdfGuardadoAsync(id, ct); }
            catch (Exception ex) { log.LogError(ex, "Falha a gerar o PDF do documento {Id} depois da emissão.", id); }
        }
        return resultado;
    }

    /// <summary>Regras fiscais verificadas antes de numerar (secção 3 da especificação).</summary>
    private async Task<List<string>> ValidarAsync(Documento doc, Serie serie, CalculoDocumento calculo, CancellationToken ct)
    {
        var erros = new List<string>();
        var rascunho = ServicoDocumentos.ParaRascunho(doc);
        erros.AddRange(await documentos.ValidarRascunhoAsync(rascunho, ct));
        erros.AddRange(calculo.Erros);
        if (doc.Linhas.Count == 0) erros.Add("O documento não tem linhas.");

        var empresa = await empresas.ObterAsync(ct);
        if (empresa.Nuit == NuitEmpresaPorPreencher) erros.Add("Preencha o NUIT da empresa em Configurações antes de emitir documentos.");

        var hoje = documentos.HojeMaputo();
        if (serie.Estado != EstadoSerie.Activa) erros.Add($"A série {serie.Descricao} está fechada.");
        if (serie.TipoDoc != doc.TipoDoc) erros.Add("A série não corresponde ao tipo de documento.");
        if (serie.Ano != doc.Data.Year) erros.Add($"A data do documento ({doc.Data:dd/MM/yyyy}) não pertence ao ano da série {serie.Descricao}.");
        if (doc.Data > hoje) erros.Add("A data do documento não pode ser futura.");
        if (serie.UltimaData is { } ultima && doc.Data < ultima)
            erros.Add($"A data não pode ser anterior à do último documento da série ({ultima:dd/MM/yyyy}).");

        var cliente = await db.Clientes.AsNoTracking().SingleOrDefaultAsync(c => c.Id == doc.ClienteId, ct);
        if (cliente is null) erros.Add("Cliente não encontrado.");
        else
        {
            if (!cliente.Activo) erros.Add("O cliente está inactivo. Reactive-o antes de emitir.");
            if (RegrasTipo.ExigeNuit(doc.TipoDoc) && string.IsNullOrEmpty(cliente.Nuit))
            {
                if (cliente.Tipo == TipoCliente.Empresa)
                    erros.Add("O NUIT do cliente é obrigatório para empresas. Preencha-o na ficha do cliente.");
                else if (empresa.LimiteNuitObrigatorio > 0 && calculo.TotalMzn > empresa.LimiteNuitObrigatorio)
                    erros.Add($"Acima de {empresa.LimiteNuitObrigatorio:#,##0.00} MT o NUIT do cliente é obrigatório. Preencha-o na ficha do cliente.");
            }
        }

        if (calculo.Linhas.Any(l => l.TaxaIvaPct == 0 && string.IsNullOrWhiteSpace(l.MotivoIsencao)))
            erros.Add("Há linhas com IVA a 0% sem motivo de isenção. Corrija a taxa em Configurações.");

        if (doc.TipoDoc == TipoDocumento.FR && doc.MetodoPagamento is null) erros.Add("Indique o método de pagamento da factura-recibo.");

        if (RegrasTipo.ExigeOrigem(doc.TipoDoc) && doc.DocOrigemId is { } origemId)
        {
            var origem = await db.Documentos.AsNoTracking().SingleOrDefaultAsync(d => d.Id == origemId, ct);
            if (origem is null || origem.Estado != EstadoDocumento.Emitido || !RegrasTipo.OrigemValida(doc.TipoDoc, origem.TipoDoc))
                erros.Add("O documento corrigido tem de ser uma factura emitida e não anulada.");
            else
            {
                if (origem.ClienteId != doc.ClienteId) erros.Add("A nota tem de ser do mesmo cliente que o documento corrigido.");
                if (origem.Moeda != doc.Moeda) erros.Add($"A nota tem de estar na moeda do documento corrigido ({origem.Moeda}).");
                if (doc.TipoDoc == TipoDocumento.NC)
                {
                    var porCreditar = await documentos.ValorPorCreditarAsync(origemId, doc.Id, ct);
                    if (calculo.Total > porCreditar)
                        erros.Add($"A nota de crédito ({calculo.Total:#,##0.00}) passa o valor ainda por creditar de {origem.NumeroFormatado} ({porCreditar:#,##0.00} {origem.Moeda}).");
                }
            }
            if (string.IsNullOrWhiteSpace(doc.MotivoCorreccao)) erros.Add("Indique o motivo da nota.");
        }
        return erros.Distinct().ToList();
    }

    // ------------------------------------------------------------------ Aprovações

    public Task<List<Aprovacao>> PendentesAsync(CancellationToken ct = default) =>
        db.Aprovacoes.AsNoTracking().Include(a => a.Documento)
            .Where(a => a.Estado == EstadoAprovacao.Pendente)
            .OrderBy(a => a.CreatedAt).ToListAsync(ct);

    public Task<int> ContarPendentesAsync(CancellationToken ct = default) =>
        db.Aprovacoes.AsNoTracking().Where(a => a.Estado == EstadoAprovacao.Pendente).Select(a => a.DocumentoId).Distinct().CountAsync(ct);

    public Task<List<Aprovacao>> AprovacoesDoDocumentoAsync(int documentoId, CancellationToken ct = default) =>
        db.Aprovacoes.AsNoTracking().Where(a => a.DocumentoId == documentoId).OrderBy(a => a.CreatedAt).ToListAsync(ct);

    /// <summary>O gestor aprova e o documento é emitido nesse momento.</summary>
    public async Task<ResultadoEmissao> AprovarAsync(int documentoId, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Documentos.Aprovar)) return ResultadoEmissao.Recusado("O seu perfil não permite aprovar documentos.");
        var doc = await db.Documentos.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentoId, ct);
        if (doc is null || doc.Estado != EstadoDocumento.AguardaAprovacao) return ResultadoEmissao.Recusado("Este documento não está a aguardar aprovação.");
        return await EmitirInternoAsync(documentoId, ct);
    }

    /// <summary>Rejeitar (gestor) ou cancelar o pedido (quem pediu): o documento volta a rascunho.</summary>
    public async Task<Resultado> DevolverARascunhoAsync(int documentoId, string? nota, bool rejeicao, CancellationToken ct = default)
    {
        if (rejeicao && !utilizador.TemPermissao(Permissoes.Documentos.Aprovar)) return Resultado.Falha("O seu perfil não permite rejeitar pedidos.");
        var doc = await db.Documentos.SingleOrDefaultAsync(d => d.Id == documentoId, ct);
        if (doc is null || doc.Estado != EstadoDocumento.AguardaAprovacao) return Resultado.Falha("Este documento não está a aguardar aprovação.");

        var pendentes = await db.Aprovacoes.Where(a => a.DocumentoId == documentoId && a.Estado == EstadoAprovacao.Pendente).ToListAsync(ct);
        if (!rejeicao && pendentes.Any(a => a.PedidoPorId != utilizador.Id) && !utilizador.TemPermissao(Permissoes.Documentos.Aprovar))
            return Resultado.Falha("Só quem fez o pedido o pode cancelar.");

        foreach (var a in pendentes)
        {
            a.Estado = EstadoAprovacao.Rejeitada;
            a.DecididoPorNome = utilizador.Nome ?? utilizador.Email;
            a.DecididoEm = relogio.GetUtcNow().UtcDateTime;
            a.NotaDecisao = rejeicao ? (string.IsNullOrWhiteSpace(nota) ? "Rejeitado" : nota.Trim()) : "Pedido cancelado por quem o fez";
        }
        doc.Estado = EstadoDocumento.Rascunho;
        auditoria.Registar(nameof(Documento), documentoId.ToString(), rejeicao ? "Documento.RejeitarAprovacao" : "Documento.CancelarPedidoAprovacao", null, new { nota });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    // ------------------------------------------------------------------ Anulação

    /// <summary>Anula com motivo. Mantém o número; só sem NC/ND emitidas, sem conversões emitidas e (fase 4) sem pagamentos.</summary>
    public async Task<Resultado> AnularAsync(int id, string motivo, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Documentos.Anular)) return Resultado.Falha("O seu perfil não permite anular documentos.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5) return Resultado.Falha("Indique o motivo da anulação (pelo menos 5 caracteres).");

        var doc = await db.Documentos.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null || await documentos.ObterAsync(id, ct) is null) return Resultado.Falha("Documento não encontrado.");
        if (doc.Estado != EstadoDocumento.Emitido) return Resultado.Falha("Só documentos emitidos podem ser anulados.");

        var correccoes = await db.Documentos.AsNoTracking()
            .Where(d => d.DocOrigemId == id && (d.Estado == EstadoDocumento.Emitido || d.Estado == EstadoDocumento.AguardaAprovacao))
            .Select(d => d.NumeroFormatado ?? "uma nota a aguardar aprovação").ToListAsync(ct);
        if (correccoes.Count > 0) return Resultado.Falha($"Não é possível anular: tem notas associadas ({string.Join(", ", correccoes)}).");

        var convertidos = await db.DocumentosOrigem.AsNoTracking().Where(o => o.OrigemId == id)
            .Join(db.Documentos, o => o.DocumentoId, d => d.Id, (o, d) => d)
            .Where(d => d.Estado == EstadoDocumento.Emitido).Select(d => d.NumeroFormatado).ToListAsync(ct);
        if (convertidos.Count > 0) return Resultado.Falha($"Não é possível anular: foi convertido em {string.Join(", ", convertidos)}. Anule primeiro esse documento.");

        doc.Estado = EstadoDocumento.Anulado;
        doc.AnuladoEm = relogio.GetUtcNow().UtcDateTime;
        doc.AnuladoPor = utilizador.Email;
        doc.MotivoAnulacao = motivo.Trim();
        auditoria.Registar(nameof(Documento), id.ToString(), "Documento.Anular", null, new { doc.NumeroFormatado, Motivo = doc.MotivoAnulacao, doc.Total, doc.Moeda });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    private static DateTime TruncarSegundos(DateTime d) => new(d.Ticks - d.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
}
