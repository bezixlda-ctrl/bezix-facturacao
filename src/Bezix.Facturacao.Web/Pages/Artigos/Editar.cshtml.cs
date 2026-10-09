using Bezix.Facturacao.Application.Artigos;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Artigos;
using Bezix.Facturacao.Domain.Fiscal;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Formatacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Artigos;

[Authorize(Policy = Permissoes.Artigos.Gerir)]
public class EditarModel(ServicoArtigos artigos, ServicoTaxasIva taxas, TimeProvider relogio) : PaginaBase
{
    public sealed class Formulario
    {
        public string Codigo { get; set; } = "";
        public string Descricao { get; set; } = "";
        public TipoArtigo Tipo { get; set; } = TipoArtigo.Servico;
        public string Unidade { get; set; } = "un";
        public decimal Preco { get; set; }
        public string CodigoTaxaIva { get; set; } = "NOR";
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public Formulario Dados { get; set; } = new();

    public bool Novo => Id is null;
    public bool Activo { get; private set; } = true;
    public List<TaxaIva> Taxas { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        await CarregarTaxasAsync();
        if (Novo) return Page();
        var a = await artigos.ObterAsync(Id!.Value);
        if (a is null) return NotFound();
        Dados = new Formulario { Codigo = a.Codigo, Descricao = a.Descricao, Tipo = a.Tipo, Unidade = a.Unidade, Preco = a.Preco, CodigoTaxaIva = a.CodigoTaxaIva };
        Activo = a.Activo;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var d = Dados;
            var (r, _) = await artigos.GuardarAsync(Id, new DadosArtigo(d.Codigo ?? "", d.Descricao ?? "", d.Tipo, d.Unidade ?? "", d.Preco, d.CodigoTaxaIva ?? ""));
            if (r.Sucesso) return RedirecionarComSucesso(Novo ? $"Artigo {d.Codigo?.ToUpperInvariant()} criado." : "Artigo guardado.", "Index");
            AdicionarErros(r);
        }
        await CarregarTaxasAsync();
        if (!Novo) Activo = (await artigos.ObterAsync(Id!.Value))?.Activo ?? true;
        return Page();
    }

    public async Task<IActionResult> OnPostEstadoAsync(bool activo)
    {
        if (Novo) return NotFound();
        var r = await artigos.DefinirActivoAsync(Id!.Value, activo);
        if (!r.Sucesso) return NotFound();
        return RedirecionarComSucesso(activo ? "Artigo reactivado." : "Artigo desactivado. Deixa de aparecer em documentos novos.", "Index");
    }

    /// <summary>Taxas em vigor hoje, mais a do artigo se já não estiver (para não a perder ao editar).</summary>
    private async Task CarregarTaxasAsync()
    {
        var hoje = Formato.HojeEmMaputo(relogio);
        var todas = await taxas.ListarAsync();
        Taxas = todas.Where(t => t.EstaVigente(hoje)).ToList();
        if (!Taxas.Any(t => t.Codigo == Dados.CodigoTaxaIva))
            Taxas.AddRange(todas.Where(t => t.Codigo == Dados.CodigoTaxaIva).Take(1));
    }
}
