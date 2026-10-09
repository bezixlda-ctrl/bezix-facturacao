using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Formatacao;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Configuracoes;

[Authorize(Policy = Permissoes.Configuracoes.VerTaxas)]
public class MoedasModel(ServicoMoedas moedas, TimeProvider relogio) : PaginaBase
{
    public List<Moeda> Moedas { get; private set; } = [];
    public List<TaxaCambio> Cambios { get; private set; } = [];
    public bool PodeGerir => User.TemPermissao(Permissoes.Configuracoes.Gerir);

    [BindProperty] public string? CambioMoeda { get; set; }
    [BindProperty] public DateOnly CambioData { get; set; }
    [BindProperty] public decimal CambioValor { get; set; }
    [BindProperty] public NovaMoedaForm Nova { get; set; } = new();

    public sealed class NovaMoedaForm
    {
        public string Codigo { get; set; } = "";
        public string Nome { get; set; } = "";
        public string Simbolo { get; set; } = "";
        public string NomeSingular { get; set; } = "";
        public string NomePlural { get; set; } = "";
    }

    public async Task OnGetAsync()
    {
        CambioData = Formato.HojeEmMaputo(relogio);
        await CarregarAsync();
    }

    public async Task<IActionResult> OnPostCambioAsync()
    {
        if (!PodeGerir) return Forbid();
        var r = await moedas.RegistarCambioAsync(CambioMoeda ?? "", CambioData, CambioValor);
        if (r.Sucesso) return RedirecionarComSucesso($"Câmbio de {CambioMoeda} em {Formato.Data(CambioData)} registado.");
        AdicionarErros(r);
        await CarregarAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostEstadoAsync(string codigo, bool activa)
    {
        if (!PodeGerir) return Forbid();
        var r = await moedas.DefinirActivaAsync(codigo, activa);
        if (r.Sucesso) return RedirecionarComSucesso(activa ? $"{codigo} activada." : $"{codigo} desactivada: deixa de aparecer em documentos novos.");
        Erro(r.Erros[0]);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCriarAsync()
    {
        if (!PodeGerir) return Forbid();
        var n = Nova;
        var r = await moedas.CriarAsync(new NovaMoeda(n.Codigo, n.Nome, n.Simbolo, n.NomeSingular, n.NomePlural, "cêntimo", "cêntimos"));
        if (r.Sucesso) return RedirecionarComSucesso($"Moeda {n.Codigo.ToUpperInvariant()} criada.");
        AdicionarErros(r);
        CambioData = Formato.HojeEmMaputo(relogio);
        await CarregarAsync();
        return Page();
    }

    private async Task CarregarAsync()
    {
        Moedas = await moedas.ListarAsync();
        Cambios = await moedas.CambiosRecentesAsync();
    }
}
