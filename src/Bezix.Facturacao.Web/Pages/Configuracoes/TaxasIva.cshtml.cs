using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Fiscal;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Formatacao;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Configuracoes;

[Authorize(Policy = Permissoes.Configuracoes.VerTaxas)]
public class TaxasIvaModel(ServicoTaxasIva taxas, TimeProvider relogio) : PaginaBase
{
    public sealed class Formulario
    {
        public string Codigo { get; set; } = "";
        public string Descricao { get; set; } = "";
        public decimal Percentagem { get; set; }
        public string? MotivoIsencao { get; set; }
        public DateOnly VigenteDesde { get; set; }
    }

    public List<TaxaIva> Taxas { get; private set; } = [];
    public DateOnly Hoje { get; private set; }
    public bool PodeGerir => User.TemPermissao(Permissoes.Configuracoes.Gerir);

    [BindProperty] public Formulario Nova { get; set; } = new();

    public async Task OnGetAsync()
    {
        Nova.VigenteDesde = Formato.HojeEmMaputo(relogio);
        await CarregarAsync();
    }

    public async Task<IActionResult> OnPostCriarAsync()
    {
        if (!PodeGerir) return Forbid();
        if (ModelState.IsValid)
        {
            var r = await taxas.CriarAsync(new NovaTaxaIva(Nova.Codigo ?? "", Nova.Descricao ?? "", Nova.Percentagem, Nova.MotivoIsencao, Nova.VigenteDesde));
            if (r.Sucesso) return RedirecionarComSucesso($"Taxa {Nova.Codigo?.ToUpperInvariant()} criada.");
            AdicionarErros(r);
        }
        await CarregarAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostFecharAsync(int id, DateOnly vigenteAte)
    {
        if (!PodeGerir) return Forbid();
        var r = await taxas.FecharAsync(id, vigenteAte);
        if (r.Sucesso) return RedirecionarComSucesso("Taxa fechada. Deixa de poder ser usada em documentos depois dessa data.");
        Erro(r.Erros[0]);
        return RedirectToPage();
    }

    private async Task CarregarAsync()
    {
        Hoje = Formato.HojeEmMaputo(relogio);
        Taxas = await taxas.ListarAsync();
    }
}
