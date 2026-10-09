using Bezix.Facturacao.Application.Artigos;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Fiscal;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Formatacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Documentos;

[Authorize(Policy = Permissoes.Documentos.Ver)]
public class EditarModel(
    ServicoDocumentos documentos,
    ServicoEmissao emissao,
    ServicoClientes clientes,
    ServicoArtigos artigos,
    ServicoTaxasIva taxas,
    Application.Abstracoes.IUtilizadorActual utilizador) : PaginaBase
{
    public sealed class LinhaForm
    {
        public int? ArtigoId { get; set; }
        public string? Ref { get; set; }
        public string? Descricao { get; set; }
        public string? Unidade { get; set; }
        public decimal Quantidade { get; set; } = 1;
        public decimal PrecoUnitario { get; set; }
        public decimal DescontoPct { get; set; }
        public string CodigoTaxaIva { get; set; } = "NOR";
    }

    public sealed class Formulario
    {
        public TipoDocumento Tipo { get; set; } = TipoDocumento.FT;
        public int SerieId { get; set; }
        public int ClienteId { get; set; }
        public DateOnly Data { get; set; }
        public DateOnly? Vencimento { get; set; }
        public string Moeda { get; set; } = Domain.Configuracao.Moeda.Base;
        public decimal Cambio { get; set; } = 1;
        public decimal DescontoGlobalPct { get; set; }
        public string? Observacoes { get; set; }
        public MetodoPagamento? MetodoPagamento { get; set; }
        public string? ReferenciaPagamento { get; set; }
        public string? LocalCarga { get; set; }
        public string? LocalDescarga { get; set; }
        public string? Matricula { get; set; }
        public int? DocOrigemId { get; set; }
        public string? MotivoCorreccao { get; set; }
        public List<int> Origens { get; set; } = [];
        public List<LinhaForm> Linhas { get; set; } = [];
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public Formulario Dados { get; set; } = new();
    [BindProperty] public string? Justificacao { get; set; }

    public CalculoDocumento? Calculo { get; private set; }
    public string? NomeCliente { get; private set; }
    public string? NuitCliente { get; private set; }
    public List<Serie> Series { get; private set; } = [];
    public List<TaxaIva> Taxas { get; private set; } = [];
    public List<Domain.Artigos.Artigo> Artigos { get; private set; } = [];
    public Documento? Origem { get; private set; }
    public decimal? PorCreditar { get; private set; }
    public decimal? LimiteDesconto => utilizador.TemPermissao(Permissoes.Documentos.DescontoSemLimite) ? null : utilizador.DescontoMaximoPct;
    public bool Novo => Id is null;

    public async Task<IActionResult> OnGetAsync(TipoDocumento? tipo, int? clienteId)
    {
        DadosRascunho d;
        if (Id is { } id)
        {
            var doc = await documentos.ObterAsync(id);
            if (doc is null) return NotFound();
            if (doc.Estado != EstadoDocumento.Rascunho) return RedirectToPage("Detalhe", new { id });
            d = ServicoDocumentos.ParaRascunho(doc);
        }
        else
        {
            var t = tipo ?? TipoDocumento.FT;
            if (RegrasTipo.ExigeOrigem(t))
            {
                Erro("As notas de crédito e de débito criam-se a partir da factura que corrigem.");
                return RedirectToPage("Index");
            }
            d = await documentos.NovoAsync(t, clienteId);
        }
        if (!documentos.PodeEditar(d.Tipo)) return Forbid();

        Dados = ParaFormulario(d);
        if (Dados.Linhas.Count == 0) Dados.Linhas.Add(new LinhaForm());
        await CarregarAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync()
    {
        var (ok, id) = await GuardarAsync();
        if (!ok) return await PaginaComErrosAsync();
        return RedirecionarComSucesso("Rascunho guardado.", "Editar", new { id });
    }

    public async Task<IActionResult> OnPostPreVisualizarAsync()
    {
        var (ok, id) = await GuardarAsync();
        if (!ok) return await PaginaComErrosAsync();
        return RedirectToPage("Detalhe", "Pdf", new { id });
    }

    public async Task<IActionResult> OnPostEmitirAsync()
    {
        var (ok, id) = await GuardarAsync();
        if (!ok) return await PaginaComErrosAsync();

        var r = await emissao.EmitirAsync(id!.Value, Justificacao);
        switch (r.Tipo)
        {
            case ResultadoEmissaoTipo.Emitido:
                return RedirecionarComSucesso($"{TipoDocumentoInfo.Nome(Dados.Tipo)} {r.NumeroFormatado} emitida.", "Detalhe", new { id });
            case ResultadoEmissaoTipo.AguardaAprovacao:
                Erro(r.Resultado.Erros[0]);
                return RedirectToPage("Detalhe", new { id });
            default:
                Id = id;
                AdicionarErros(r.Resultado);
                return await PaginaComErrosAsync();
        }
    }

    /// <summary>Totais ao vivo (htmx). O servidor é a única fonte do cálculo.</summary>
    public async Task<IActionResult> OnPostCalcularAsync()
    {
        Calculo = await documentos.CalcularAsync(ParaRascunho(Dados));
        return Partial("_Totais", this);
    }

    public async Task<IActionResult> OnGetClientesAsync(string? q)
    {
        var lista = string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2
            ? []
            : (await clientes.PesquisarAsync(new FiltroClientes(q, Tamanho: 8))).Itens;
        return Partial("_EscolherCliente", lista);
    }

    public async Task<IActionResult> OnGetClienteAsync(int id)
    {
        var c = await clientes.ObterAsync(id);
        if (c is null) return NotFound();
        return new JsonResult(new { c.Id, c.Nome, c.Nuit, prazo = c.PrazoPagamentoDias, desconto = Formato.Numero(c.DescontoPadraoPct) });
    }

    public async Task<IActionResult> OnGetArtigoAsync(string codigo)
    {
        var lista = await artigos.PesquisarAsync(new FiltroArtigos(codigo, Tamanho: 5));
        var a = lista.Itens.FirstOrDefault(x => string.Equals(x.Codigo, codigo?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (a is null) return NotFound();
        return new JsonResult(new { a.Id, a.Codigo, a.Descricao, a.Unidade, preco = Formato.Numero(a.Preco), taxa = a.CodigoTaxaIva });
    }

    // ------------------------------------------------------------------

    private async Task<(bool Ok, int? Id)> GuardarAsync()
    {
        if (!ModelState.IsValid) return (false, Id);
        // Linhas totalmente vazias (a linha em branco do editor) são ignoradas.
        Dados.Linhas = Dados.Linhas.Where(l => !string.IsNullOrWhiteSpace(l.Descricao) || !string.IsNullOrWhiteSpace(l.Ref) || l.PrecoUnitario != 0).ToList();
        var (r, id) = await documentos.GuardarRascunhoAsync(Id, ParaRascunho(Dados));
        if (!r.Sucesso)
        {
            AdicionarErros(r);
            return (false, Id);
        }
        Id = id;
        return (true, id);
    }

    private async Task<IActionResult> PaginaComErrosAsync()
    {
        if (Dados.Linhas.Count == 0) Dados.Linhas.Add(new LinhaForm());
        await CarregarAsync();
        return Page();
    }

    private async Task CarregarAsync()
    {
        Series = await documentos.SeriesActivasAsync(Dados.Tipo, Dados.Data.Year);
        Taxas = await taxas.VigentesAsync(Dados.Data);
        Artigos = (await artigos.PesquisarAsync(new Application.Artigos.FiltroArtigos(Tamanho: 100))).Itens.ToList();
        if (Dados.ClienteId > 0 && await clientes.ObterAsync(Dados.ClienteId) is { } c)
        {
            NomeCliente = c.Nome;
            NuitCliente = c.Nuit;
        }
        if (Dados.DocOrigemId is { } o)
        {
            Origem = await documentos.ObterAsync(o);
            if (Origem is not null && Dados.Tipo == TipoDocumento.NC) PorCreditar = await documentos.ValorPorCreditarAsync(o, Id);
        }
        Calculo = await documentos.CalcularAsync(ParaRascunho(Dados));
    }

    private static DadosRascunho ParaRascunho(Formulario f) => new(
        f.Tipo, f.SerieId, f.ClienteId, f.Data, f.Vencimento, f.Moeda ?? Domain.Configuracao.Moeda.Base, f.Cambio, f.DescontoGlobalPct,
        f.Observacoes, f.MetodoPagamento, f.ReferenciaPagamento, f.LocalCarga, f.LocalDescarga, f.Matricula, f.DocOrigemId, f.MotivoCorreccao,
        f.Linhas.Select(l => new LinhaRascunho(l.ArtigoId, l.Ref, l.Descricao ?? "", l.Unidade, l.Quantidade, l.PrecoUnitario, l.DescontoPct, l.CodigoTaxaIva ?? "")).ToList(),
        f.Origens);

    private static Formulario ParaFormulario(DadosRascunho d) => new()
    {
        Tipo = d.Tipo, SerieId = d.SerieId, ClienteId = d.ClienteId, Data = d.Data, Vencimento = d.Vencimento, Moeda = d.Moeda,
        Cambio = d.Cambio, DescontoGlobalPct = d.DescontoGlobalPct, Observacoes = d.Observacoes, MetodoPagamento = d.MetodoPagamento,
        ReferenciaPagamento = d.ReferenciaPagamento, LocalCarga = d.LocalCarga, LocalDescarga = d.LocalDescarga, Matricula = d.Matricula,
        DocOrigemId = d.DocOrigemId, MotivoCorreccao = d.MotivoCorreccao, Origens = d.Origens?.ToList() ?? [],
        Linhas = d.Linhas.Select(l => new LinhaForm
        {
            ArtigoId = l.ArtigoId, Ref = l.Ref, Descricao = l.Descricao, Unidade = l.Unidade, Quantidade = l.Quantidade,
            PrecoUnitario = l.PrecoUnitario, DescontoPct = l.DescontoPct, CodigoTaxaIva = l.CodigoTaxaIva,
        }).ToList(),
    };
}
