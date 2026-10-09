using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Formatacao;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Configuracoes;

[Authorize(Policy = Permissoes.Configuracoes.VerTaxas)]
public class SeriesModel(ServicoSeries series, TimeProvider relogio) : PaginaBase
{
    public sealed class Formulario
    {
        public TipoDocumento TipoDoc { get; set; } = TipoDocumento.FT;
        public string Codigo { get; set; } = "";
        public int Ano { get; set; }
        public DateOnly DataAbertura { get; set; }
    }

    public List<Serie> Series { get; private set; } = [];
    public DateOnly Hoje { get; private set; }
    public bool PodeGerir => User.TemPermissao(Permissoes.Configuracoes.Gerir);

    [BindProperty] public Formulario Nova { get; set; } = new();

    public async Task OnGetAsync()
    {
        Hoje = Formato.HojeEmMaputo(relogio);
        Nova.Ano = Hoje.Year;
        Nova.DataAbertura = Hoje;
        Series = await series.ListarAsync();
    }

    public async Task<IActionResult> OnPostCriarAsync()
    {
        if (!PodeGerir) return Forbid();
        if (ModelState.IsValid)
        {
            var r = await series.CriarAsync(new NovaSerie(Nova.TipoDoc, Nova.Codigo ?? "", Nova.Ano, Nova.DataAbertura));
            if (r.Sucesso) return RedirecionarComSucesso($"Série {Nova.TipoDoc} {Nova.Codigo?.ToUpperInvariant()}{Nova.Ano} aberta.");
            AdicionarErros(r);
        }
        Hoje = Formato.HojeEmMaputo(relogio);
        Series = await series.ListarAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostFecharAsync(int id, DateOnly dataFecho)
    {
        if (!PodeGerir) return Forbid();
        var r = await series.FecharAsync(id, dataFecho);
        if (r.Sucesso) return RedirecionarComSucesso("Série fechada. Não aceita mais documentos.");
        Erro(r.Erros[0]);
        return RedirectToPage();
    }
}
