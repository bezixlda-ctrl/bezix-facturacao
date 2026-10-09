using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Comunicacao.Modelos;

[Authorize(Policy = Permissoes.Comunicacao.GerirModelos)]
public class EditarModel(ServicoComunicacao comunicacao) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public string Codigo { get; set; } = "";
    [BindProperty] public string Assunto { get; set; } = "";
    [BindProperty] public string Corpo { get; set; } = "";
    [BindProperty] public string? Cc { get; set; }
    [BindProperty] public bool AnexarPdf { get; set; }
    [BindProperty] public bool Activo { get; set; }

    public ModeloComunicacao Modelo { get; private set; } = null!;
    public bool TemOriginal => ModelosPadrao.Obter(Codigo) is not null;

    // Pré-visualização
    public string? AssuntoPrevisto { get; private set; }
    public string? CorpoPrevisto { get; private set; }
    public string? Origem { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CarregarAsync()) return NotFound();
        Assunto = Modelo.Assunto;
        Corpo = Modelo.Corpo;
        Cc = Modelo.Cc;
        AnexarPdf = Modelo.AnexarPdf;
        Activo = Modelo.Activo;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await CarregarAsync()) return NotFound();
        var r = await comunicacao.GuardarModeloAsync(Codigo, Assunto, Corpo, Cc, AnexarPdf, Activo);
        if (r.Sucesso) return RedirecionarComSucesso("Modelo guardado. Os próximos emails já usam este texto.", "Index");
        AdicionarErros(r);
        return Page();
    }

    public async Task<IActionResult> OnPostPrevisualizarAsync()
    {
        var (a, html, origem) = await comunicacao.PrevisualizarAsync(Codigo, Assunto ?? "", Corpo ?? "");
        AssuntoPrevisto = a;
        CorpoPrevisto = html;
        Origem = origem;
        return Partial("_Previsualizacao", this);
    }

    public async Task<IActionResult> OnPostTesteAsync()
    {
        if (!await CarregarAsync()) return NotFound();
        var r = await comunicacao.EnviarTesteAsync(Codigo, Assunto ?? "", Corpo ?? "");
        if (r.Sucesso) Sucesso("Email de teste enviado para o seu endereço.");
        else AdicionarErros(r);
        return Page();
    }

    public async Task<IActionResult> OnPostReporAsync()
    {
        var r = await comunicacao.ReporModeloAsync(Codigo);
        if (r.Sucesso) return RedirecionarComSucesso("Texto original reposto.", null, new { Codigo });
        Erro(string.Join(" ", r.Erros));
        return RedirectToPage(new { Codigo });
    }

    private async Task<bool> CarregarAsync()
    {
        var m = await comunicacao.ModeloAsync(Codigo);
        if (m is null) return false;
        Modelo = m;
        return true;
    }
}
