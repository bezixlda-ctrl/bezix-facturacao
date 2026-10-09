using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Fiscal;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Documentos;

/// <summary>Consulta de documentos, rascunhos e cálculo. A emissão está em <see cref="ServicoEmissao"/>.</summary>
public sealed class ServicoDocumentos(
    IAppDbContext db,
    IUtilizadorActual utilizador,
    ServicoEmpresa empresas,
    ServicoClientes clientes,
    IServicoAuditoria auditoria,
    TimeProvider relogio)
{
    public const int MaximoLinhas = 300;

    // ------------------------------------------------------------------ Permissões

    /// <summary>Pode criar ou alterar rascunhos deste tipo.</summary>
    public bool PodeEditar(TipoDocumento t) =>
        utilizador.TemPermissao(Permissoes.Documentos.Rascunho) || PodeEmitir(t)
        || (t == TipoDocumento.NC && utilizador.TemPermissao(Permissoes.Documentos.PedirNotaCredito));

    /// <summary>Pode emitir directamente (sem pedir aprovação por causa do tipo).</summary>
    public bool PodeEmitir(TipoDocumento t) => t switch
    {
        TipoDocumento.FT or TipoDocumento.FG => utilizador.TemPermissao(Permissoes.Documentos.EmitirFactura),
        TipoDocumento.FR => utilizador.TemPermissao(Permissoes.Documentos.EmitirFacturaRecibo),
        TipoDocumento.ND => utilizador.TemPermissao(Permissoes.Documentos.EmitirNotaDebito),
        TipoDocumento.NC => utilizador.TemPermissao(Permissoes.Documentos.EmitirNotaCredito),
        TipoDocumento.CT or TipoDocumento.GR => utilizador.TemPermissao(Permissoes.Documentos.Rascunho),
        _ => false,
    };

    public IEnumerable<TipoDocumento> TiposEditaveis() => RegrasTipo.Editaveis.Where(PodeEditar);

    // ------------------------------------------------------------------ Consulta

    public async Task<IQueryable<Documento>> VisiveisAsync(CancellationToken ct = default)
    {
        var q = db.Documentos.AsQueryable();
        if (await clientes.SoProprioAsync(ct))
        {
            var id = utilizador.Id;
            q = q.Where(d => d.ResponsavelId == id || db.Clientes.Any(c => c.Id == d.ClienteId && c.ResponsavelId == id));
        }
        return q;
    }

    public async Task<Pagina<DocumentoResumo>> PesquisarAsync(FiltroDocumentos f, CancellationToken ct = default)
    {
        var (numero, tamanho) = Pagina<DocumentoResumo>.Normalizar(f.Pagina, f.Tamanho);
        var q = (await VisiveisAsync(ct)).AsNoTracking();
        if (f.Tipo is { } t) q = q.Where(d => d.TipoDoc == t);
        if (f.Estado is { } e) q = q.Where(d => d.Estado == e);
        if (f.ClienteId is { } c) q = q.Where(d => d.ClienteId == c);
        if (f.De is { } de) q = q.Where(d => d.Data >= de);
        if (f.Ate is { } ate) q = q.Where(d => d.Data <= ate);
        if (!string.IsNullOrWhiteSpace(f.Texto))
        {
            var texto = f.Texto.Trim();
            q = q.Where(d => (d.NumeroFormatado != null && d.NumeroFormatado.Contains(texto)) || d.ClienteNome.Contains(texto)
                || (d.ClienteNuit != null && d.ClienteNuit.Contains(texto)));
        }

        var total = await q.CountAsync(ct);
        var itens = await q
            .OrderByDescending(d => d.Estado == EstadoDocumento.Rascunho || d.Estado == EstadoDocumento.AguardaAprovacao)
            .ThenByDescending(d => d.Data).ThenByDescending(d => d.Id)
            .Skip((numero - 1) * tamanho).Take(tamanho)
            .Select(d => new DocumentoResumo(d.Id, d.TipoDoc, d.NumeroFormatado, d.Estado, d.Data, d.Vencimento, d.ClienteId, d.ClienteNome, d.Moeda, d.Total, d.TotalMzn))
            .ToListAsync(ct);
        return new Pagina<DocumentoResumo>(itens, total, numero, tamanho);
    }

    public async Task<Documento?> ObterAsync(int id, CancellationToken ct = default) =>
        await (await VisiveisAsync(ct)).AsNoTracking()
            .Include(d => d.Linhas.OrderBy(l => l.Ordem))
            .Include(d => d.IvaResumo)
            .Include(d => d.Serie)
            .Include(d => d.DocOrigem)
            .Include(d => d.Origens).ThenInclude(o => o.Origem)
            .SingleOrDefaultAsync(d => d.Id == id, ct);

    /// <summary>NC e ND emitidas que corrigem este documento.</summary>
    public Task<List<DocumentoResumo>> CorreccoesAsync(int origemId, CancellationToken ct = default) =>
        db.Documentos.AsNoTracking().Where(d => d.DocOrigemId == origemId)
            .OrderBy(d => d.Id)
            .Select(d => new DocumentoResumo(d.Id, d.TipoDoc, d.NumeroFormatado, d.Estado, d.Data, d.Vencimento, d.ClienteId, d.ClienteNome, d.Moeda, d.Total, d.TotalMzn))
            .ToListAsync(ct);

    /// <summary>Documentos gerados a partir deste por conversão.</summary>
    public Task<List<DocumentoResumo>> ConvertidosEmAsync(int origemId, CancellationToken ct = default) =>
        db.DocumentosOrigem.AsNoTracking().Where(o => o.OrigemId == origemId)
            .Join(db.Documentos, o => o.DocumentoId, d => d.Id, (o, d) => d)
            .Select(d => new DocumentoResumo(d.Id, d.TipoDoc, d.NumeroFormatado, d.Estado, d.Data, d.Vencimento, d.ClienteId, d.ClienteNome, d.Moeda, d.Total, d.TotalMzn))
            .ToListAsync(ct);

    /// <summary>Valor (na moeda do documento) ainda por creditar: total menos as NC emitidas que o referenciam.</summary>
    public async Task<decimal> ValorPorCreditarAsync(int origemId, int? excluirNcId = null, CancellationToken ct = default)
    {
        var origem = await db.Documentos.AsNoTracking().SingleAsync(d => d.Id == origemId, ct);
        var creditado = await db.Documentos.AsNoTracking()
            .Where(d => d.DocOrigemId == origemId && d.TipoDoc == TipoDocumento.NC && d.Id != excluirNcId
                && (d.Estado == EstadoDocumento.Emitido || d.Estado == EstadoDocumento.AguardaAprovacao))
            .SumAsync(d => (decimal?)d.Total, ct) ?? 0;
        return origem.Total - creditado;
    }

    public Task<List<Serie>> SeriesActivasAsync(TipoDocumento tipo, int ano, CancellationToken ct = default) =>
        db.Series.AsNoTracking().Where(s => s.TipoDoc == tipo && s.Ano == ano && s.Estado == EstadoSerie.Activa)
            .OrderBy(s => s.Codigo).ToListAsync(ct);

    // ------------------------------------------------------------------ Cálculo

    public async Task<CalculoDocumento> CalcularAsync(DadosRascunho d, CancellationToken ct = default)
    {
        var empresa = await empresas.ObterAsync(ct);
        var taxas = await db.TaxasIva.AsNoTracking().ToListAsync(ct);
        return Calcular(d, taxas, empresa.ModoArredondamentoIva);
    }

    /// <summary>Cálculo puro: taxas resolvidas pela data do documento; contravalor em MZN pelo câmbio.</summary>
    public static CalculoDocumento Calcular(DadosRascunho d, IReadOnlyList<TaxaIva> taxas, ModoArredondamentoIva modo)
    {
        var erros = new List<string>();
        var resolvidas = new List<(LinhaRascunho Linha, decimal Pct, string? Motivo)>();
        foreach (var l in d.Linhas)
        {
            var taxa = taxas.FirstOrDefault(t => t.Codigo == l.CodigoTaxaIva && t.EstaVigente(d.Data));
            if (taxa is null)
            {
                erros.Add($"A taxa de IVA \"{l.CodigoTaxaIva}\" não está em vigor em {d.Data:dd/MM/yyyy} (linha \"{Truncar(l.Descricao)}\").");
                resolvidas.Add((l, 0, null));
            }
            else resolvidas.Add((l, taxa.Percentagem, taxa.IsIsencao ? taxa.MotivoIsencao : null));
        }

        Domain.Fiscal.ResultadoCalculo r;
        try
        {
            r = CalculadoraDocumento.Calcular(
                resolvidas.Select(x => new Domain.Fiscal.LinhaCalculo(x.Linha.Quantidade, x.Linha.PrecoUnitario, x.Linha.DescontoPct, x.Pct)).ToList(),
                d.DescontoGlobalPct, modo);
        }
        catch (ArgumentException ex)
        {
            erros.Add(ex.Message.Split(" (Parameter")[0]);
            return new CalculoDocumento([], 0, d.DescontoGlobalPct, 0, 0, [], 0, 0, 0, 0, 0, 0, erros);
        }

        var cambio = d.Moeda == Moeda.Base ? 1m : d.Cambio;
        var linhas = resolvidas.Select((x, i) => new LinhaCalculo(x.Linha, x.Pct, x.Motivo, r.Linhas[i].Base, r.Linhas[i].Iva, r.Linhas[i].TotalLinha)).ToList();
        var resumo = r.IvaPorTaxa.Select(t => new ResumoTaxa(
            t.TaxaPct,
            t.TaxaPct == 0 ? string.Join("; ", resolvidas.Where(x => x.Pct == 0 && x.Motivo != null).Select(x => x.Motivo).Distinct()) is { Length: > 0 } m ? m : null : null,
            t.Base, t.Iva,
            CalculadoraDocumento.Arredondar(t.Base * cambio),
            CalculadoraDocumento.Arredondar(t.Iva * cambio))).ToList();

        var baseMzn = resumo.Sum(t => t.BaseMzn);
        var ivaMzn = resumo.Sum(t => t.IvaMzn);
        var bruto = d.Linhas.Sum(l => CalculadoraDocumento.Arredondar(l.Quantidade * l.PrecoUnitario));
        var descontoEfectivo = bruto > 0 ? Math.Round((bruto - r.BaseTributavel) / bruto * 100m, 2) : 0;

        return new CalculoDocumento(linhas, r.Subtotal, r.DescontoGlobalPct, r.DescontoGlobal, r.BaseTributavel, resumo,
            r.TotalIva, r.Total, baseMzn, ivaMzn, baseMzn + ivaMzn, descontoEfectivo, erros);
    }

    // ------------------------------------------------------------------ Rascunhos

    public async Task<DadosRascunho> NovoAsync(TipoDocumento tipo, int? clienteId = null, CancellationToken ct = default)
    {
        var hoje = HojeMaputo();
        var serie = (await SeriesActivasAsync(tipo, hoje.Year, ct)).FirstOrDefault();
        var empresa = await empresas.ObterAsync(ct);
        int? prazo = null;
        decimal desconto = 0;
        if (clienteId is { } id && await clientes.ObterAsync(id, ct) is { } c)
        {
            prazo = c.PrazoPagamentoDias;
            desconto = c.DescontoPadraoPct;
        }
        var vencimento = RegrasTipo.TemVencimento(tipo) ? hoje.AddDays(prazo ?? empresa.PrazoPagamentoDias) : (DateOnly?)null;
        return new DadosRascunho(tipo, serie?.Id ?? 0, clienteId ?? 0, hoje, vencimento, Moeda.Base, 1m, desconto,
            null, null, null, null, null, null, null, null, []);
    }

    public async Task<(Resultado Resultado, int? Id)> GuardarRascunhoAsync(int? id, DadosRascunho d, CancellationToken ct = default)
    {
        if (!PodeEditar(d.Tipo)) return (Resultado.Falha("O seu perfil não permite criar este tipo de documento."), id);

        var erros = await ValidarRascunhoAsync(d, ct);
        if (erros.Count > 0) return (Resultado.Falha([.. erros]), id);

        var calculo = await CalcularAsync(d, ct);
        if (calculo.Linhas.Count == 0 && d.Linhas.Count > 0) return (Resultado.Falha([.. calculo.Erros]), id);

        Documento doc;
        if (id is null)
        {
            doc = new Documento { ResponsavelId = utilizador.Id };
            db.Documentos.Add(doc);
        }
        else
        {
            doc = await (await VisiveisAsync(ct)).Include(x => x.Linhas).Include(x => x.IvaResumo).Include(x => x.Origens)
                .SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Documento não encontrado.");
            if (doc.Estado != EstadoDocumento.Rascunho)
                return (Resultado.Falha("Só os rascunhos podem ser alterados. Um documento emitido corrige-se com nota de crédito ou débito."), id);
            if (doc.TipoDoc != d.Tipo) return (Resultado.Falha("Não é possível mudar o tipo de um rascunho. Crie um documento novo."), id);
        }

        var cliente = (await clientes.ObterAsync(d.ClienteId, ct))!;
        await AplicarAsync(doc, d, calculo, cliente, ct);
        await db.SaveChangesAsync(ct);
        if (id is null)
        {
            auditoria.Registar(nameof(Documento), doc.Id.ToString(), "Documento.CriarRascunho", null, new { Tipo = doc.TipoDoc.ToString(), doc.ClienteNome });
            await db.SaveChangesAsync(ct);
        }
        return (Resultado.Ok(), doc.Id);
    }

    public async Task<Resultado> ApagarRascunhoAsync(int id, CancellationToken ct = default)
    {
        var doc = await (await VisiveisAsync(ct)).Include(d => d.Linhas).Include(d => d.IvaResumo).Include(d => d.Origens)
            .SingleOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null) return Resultado.Falha("Documento não encontrado.");
        if (doc.Estado != EstadoDocumento.Rascunho) return Resultado.Falha("Só os rascunhos podem ser apagados.");
        if (!PodeEditar(doc.TipoDoc)) return Resultado.Falha("O seu perfil não permite alterar este tipo de documento.");

        db.Aprovacoes.RemoveRange(db.Aprovacoes.Where(a => a.DocumentoId == id));
        db.DocumentoLinhas.RemoveRange(doc.Linhas);
        db.DocumentoIvaResumos.RemoveRange(doc.IvaResumo);
        db.DocumentosOrigem.RemoveRange(doc.Origens);
        db.Documentos.Remove(doc);
        auditoria.Registar(nameof(Documento), id.ToString(), "Documento.ApagarRascunho", new { Tipo = doc.TipoDoc.ToString(), doc.ClienteNome, doc.Total });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>Copia um documento (emitido ou não) para um rascunho novo com a data de hoje.</summary>
    public async Task<(Resultado Resultado, int? Id)> DuplicarAsync(int id, CancellationToken ct = default)
    {
        var o = await ObterAsync(id, ct);
        if (o is null) return (Resultado.Falha("Documento não encontrado."), null);
        var tipo = o.TipoDoc is TipoDocumento.NC or TipoDocumento.ND ? TipoDocumento.FT : o.TipoDoc;
        var novo = await NovoAsync(tipo, o.ClienteId, ct);
        return await GuardarRascunhoAsync(null, novo with
        {
            DescontoGlobalPct = o.DescontoGlobalPct,
            Observacoes = o.Observacoes,
            Linhas = CopiarLinhas(o),
        }, ct);
    }

    /// <summary>CT → FT/FR ou GR(s) → FT/FG: novo rascunho com as linhas copiadas e ligação para rastreio.</summary>
    public async Task<(Resultado Resultado, int? Id)> ConverterAsync(IReadOnlyList<int> origemIds, TipoDocumento destino, CancellationToken ct = default)
    {
        if (origemIds.Count == 0) return (Resultado.Falha("Escolha pelo menos um documento."), null);
        var origens = new List<Documento>();
        foreach (var oid in origemIds.Distinct())
        {
            var o = await ObterAsync(oid, ct);
            if (o is null) return (Resultado.Falha("Documento de origem não encontrado."), null);
            if (o.Estado != EstadoDocumento.Emitido) return (Resultado.Falha($"{o.NumeroFormatado ?? "O rascunho"} não está emitido: só documentos emitidos se convertem."), null);
            if (!RegrasTipo.ConverteEm(o.TipoDoc).Contains(destino)) return (Resultado.Falha($"Um documento {o.TipoDoc} não se converte em {destino}."), null);
            origens.Add(o);
        }
        if (origens.Select(o => o.ClienteId).Distinct().Count() > 1) return (Resultado.Falha("Todos os documentos têm de ser do mesmo cliente."), null);
        if (origens.Select(o => o.Moeda).Distinct().Count() > 1) return (Resultado.Falha("Todos os documentos têm de estar na mesma moeda."), null);
        if (origens.Count > 1 && destino != TipoDocumento.FG && destino != TipoDocumento.FT) return (Resultado.Falha("Várias guias só se agregam numa factura ou factura global."), null);

        var primeira = origens[0];
        var novo = await NovoAsync(destino, primeira.ClienteId, ct);
        var observacoes = "Ref.: " + string.Join(", ", origens.Select(o => o.NumeroFormatado));
        return await GuardarRascunhoAsync(null, novo with
        {
            DescontoGlobalPct = origens.Count == 1 ? primeira.DescontoGlobalPct : 0,
            Observacoes = observacoes,
            Linhas = origens.SelectMany(CopiarLinhas).ToList(),
            Origens = origens.Select(o => o.Id).ToList(),
        }, ct);
    }

    /// <summary>NC (linhas copiadas para ajustar) ou ND (vazia) a partir de um documento emitido.</summary>
    public async Task<(Resultado Resultado, int? Id)> CriarCorreccaoAsync(int origemId, TipoDocumento tipo, CancellationToken ct = default)
    {
        if (!RegrasTipo.ExigeOrigem(tipo)) return (Resultado.Falha("Só NC e ND corrigem documentos."), null);
        var o = await ObterAsync(origemId, ct);
        if (o is null) return (Resultado.Falha("Documento não encontrado."), null);
        if (o.Estado != EstadoDocumento.Emitido || !RegrasTipo.OrigemValida(tipo, o.TipoDoc))
            return (Resultado.Falha($"Só se emite {TipoDocumentoInfo.Nome(tipo).ToLowerInvariant()} sobre facturas emitidas e não anuladas."), null);

        var novo = await NovoAsync(tipo, o.ClienteId, ct);
        var linhas = tipo == TipoDocumento.NC ? CopiarLinhas(o) : [];
        return await GuardarRascunhoAsync(null, novo with
        {
            DescontoGlobalPct = tipo == TipoDocumento.NC ? o.DescontoGlobalPct : 0,
            DocOrigemId = o.Id,
            Linhas = linhas,
        }, ct);
    }

    public static DadosRascunho ParaRascunho(Documento d) => new(
        d.TipoDoc, d.SerieId, d.ClienteId, d.Data, d.Vencimento, d.Moeda, d.Cambio, d.DescontoGlobalPct, d.Observacoes,
        d.MetodoPagamento, d.ReferenciaPagamento, d.LocalCarga, d.LocalDescarga, d.Matricula, d.DocOrigemId, d.MotivoCorreccao,
        CopiarLinhas(d), d.Origens.Select(o => o.OrigemId).ToList());

    // ------------------------------------------------------------------ Auxiliares

    internal async Task<List<string>> ValidarRascunhoAsync(DadosRascunho d, CancellationToken ct)
    {
        var erros = new List<string>();
        if (!RegrasTipo.Editaveis.Contains(d.Tipo)) erros.Add("Tipo de documento inválido.");

        var serie = await db.Series.AsNoTracking().SingleOrDefaultAsync(s => s.Id == d.SerieId, ct);
        if (serie is null || serie.TipoDoc != d.Tipo) erros.Add($"Escolha uma série de {TipoDocumentoInfo.Nome(d.Tipo).ToLowerInvariant()}.");

        if (d.ClienteId == 0 || await clientes.ObterAsync(d.ClienteId, ct) is null) erros.Add("Escolha o cliente.");

        // Legislação fiscal moçambicana: os documentos são emitidos em meticais.
        if (d.Moeda != Moeda.Base || d.Cambio != 1m) erros.Add("Os documentos são emitidos em meticais (MZN).");

        if (d.Vencimento is { } v && v < d.Data) erros.Add("O vencimento não pode ser anterior à data do documento.");
        if (d.DescontoGlobalPct is < 0 or > 100) erros.Add("O desconto global tem de estar entre 0 e 100%.");
        if (d.Linhas.Count > MaximoLinhas) erros.Add($"Um documento pode ter no máximo {MaximoLinhas} linhas.");

        for (var i = 0; i < d.Linhas.Count; i++)
        {
            var l = d.Linhas[i];
            var n = i + 1;
            if (string.IsNullOrWhiteSpace(l.Descricao)) erros.Add($"Linha {n}: indique a descrição.");
            else if (l.Descricao.Length > 500) erros.Add($"Linha {n}: a descrição pode ter no máximo 500 caracteres.");
            if (l.Quantidade <= 0) erros.Add($"Linha {n}: a quantidade tem de ser maior que zero.");
            if (decimal.Round(l.Quantidade, 4) != l.Quantidade) erros.Add($"Linha {n}: a quantidade pode ter no máximo 4 casas decimais.");
            if (l.PrecoUnitario < 0) erros.Add($"Linha {n}: o preço não pode ser negativo.");
            if (decimal.Round(l.PrecoUnitario, 2) != l.PrecoUnitario) erros.Add($"Linha {n}: o preço só pode ter 2 casas decimais.");
            if (l.DescontoPct is < 0 or > 100) erros.Add($"Linha {n}: o desconto tem de estar entre 0 e 100%.");
            if (string.IsNullOrWhiteSpace(l.CodigoTaxaIva)) erros.Add($"Linha {n}: escolha a taxa de IVA.");
        }

        if (RegrasTipo.ExigeOrigem(d.Tipo) && d.DocOrigemId is null) erros.Add("Indique o documento que esta nota corrige.");
        return erros;
    }

    private async Task AplicarAsync(Documento doc, DadosRascunho d, CalculoDocumento c, Domain.Clientes.Cliente cliente, CancellationToken ct)
    {
        doc.TipoDoc = d.Tipo;
        doc.SerieId = d.SerieId;
        doc.Data = d.Data;
        doc.Vencimento = RegrasTipo.TemVencimento(d.Tipo) ? d.Vencimento : null;
        doc.ClienteId = cliente.Id;
        CopiarCliente(doc, cliente);
        doc.Moeda = d.Moeda;
        doc.Cambio = d.Moeda == Moeda.Base ? 1m : d.Cambio;
        doc.DescontoGlobalPct = d.DescontoGlobalPct;
        doc.Observacoes = Limpar(d.Observacoes);
        doc.MetodoPagamento = d.Tipo == TipoDocumento.FR ? d.MetodoPagamento : null;
        doc.ReferenciaPagamento = d.Tipo == TipoDocumento.FR ? Limpar(d.ReferenciaPagamento) : null;
        doc.LocalCarga = d.Tipo == TipoDocumento.GR ? Limpar(d.LocalCarga) : null;
        doc.LocalDescarga = d.Tipo == TipoDocumento.GR ? Limpar(d.LocalDescarga) : null;
        doc.Matricula = d.Tipo == TipoDocumento.GR ? Limpar(d.Matricula)?.ToUpperInvariant() : null;
        doc.DocOrigemId = RegrasTipo.ExigeOrigem(d.Tipo) ? d.DocOrigemId : null;
        doc.MotivoCorreccao = RegrasTipo.ExigeOrigem(d.Tipo) ? Limpar(d.MotivoCorreccao) : null;
        AplicarCalculo(doc, c);

        if (d.Origens is { Count: > 0 } && doc.Origens.Count == 0)
            foreach (var o in d.Origens.Distinct()) doc.Origens.Add(new DocumentoOrigem { OrigemId = o });
        await Task.CompletedTask;
    }

    /// <summary>Substitui linhas, resumo de IVA e totais pelo resultado do cálculo.</summary>
    internal void AplicarCalculo(Documento doc, CalculoDocumento c)
    {
        foreach (var l in doc.Linhas.ToList()) db.DocumentoLinhas.Remove(l);
        foreach (var r in doc.IvaResumo.ToList()) db.DocumentoIvaResumos.Remove(r);
        doc.Linhas.Clear();
        doc.IvaResumo.Clear();

        var ordem = 1;
        foreach (var l in c.Linhas)
        {
            doc.Linhas.Add(new DocumentoLinha
            {
                Ordem = ordem++,
                ArtigoId = l.Linha.ArtigoId,
                Ref = Limpar(l.Linha.Ref)?.ToUpperInvariant(),
                Descricao = l.Linha.Descricao.Trim(),
                Unidade = Limpar(l.Linha.Unidade),
                Quantidade = l.Linha.Quantidade,
                PrecoUnitario = l.Linha.PrecoUnitario,
                DescontoPct = l.Linha.DescontoPct,
                CodigoTaxaIva = l.Linha.CodigoTaxaIva.Trim().ToUpperInvariant(),
                TaxaIvaPct = l.TaxaIvaPct,
                MotivoIsencao = l.MotivoIsencao,
                Base = l.Base,
                Iva = l.Iva,
                TotalLinha = l.TotalLinha,
            });
        }
        foreach (var r in c.IvaPorTaxa)
            doc.IvaResumo.Add(new DocumentoIvaResumo { TaxaPct = r.TaxaPct, MotivoIsencao = r.MotivoIsencao, Base = r.Base, Iva = r.Iva, BaseMzn = r.BaseMzn, IvaMzn = r.IvaMzn });

        doc.Subtotal = c.Subtotal;
        doc.DescontoGlobal = c.DescontoGlobal;
        doc.BaseTributavel = c.BaseTributavel;
        doc.TotalIva = c.TotalIva;
        doc.Total = c.Total;
        doc.BaseTributavelMzn = c.BaseTributavelMzn;
        doc.TotalIvaMzn = c.TotalIvaMzn;
        doc.TotalMzn = c.TotalMzn;
    }

    internal static void CopiarCliente(Documento doc, Domain.Clientes.Cliente c)
    {
        doc.ClienteNome = c.Nome;
        doc.ClienteNuit = c.Nuit;
        doc.ClienteEndereco = c.Endereco;
        doc.ClienteCidade = c.Cidade;
        doc.ClienteTelefone = c.Telefone;
        doc.ClienteCelular = c.Celular;
        doc.ClienteContacto = c.Contacto;
        doc.ClienteEmail = c.EmailEnvio;
    }

    private static List<LinhaRascunho> CopiarLinhas(Documento d) =>
        d.Linhas.OrderBy(l => l.Ordem)
            .Select(l => new LinhaRascunho(l.ArtigoId, l.Ref, l.Descricao, l.Unidade, l.Quantidade, l.PrecoUnitario, l.DescontoPct, l.CodigoTaxaIva))
            .ToList();

    internal DateOnly HojeMaputo() =>
        DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime.AddHours(2)); // Africa/Maputo é UTC+2 todo o ano (sem horário de Verão)

    private static string? Limpar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string Truncar(string s) => s.Length > 40 ? s[..40] + "…" : s;
}
