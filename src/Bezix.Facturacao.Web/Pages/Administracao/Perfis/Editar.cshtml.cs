using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Administracao.Perfis;

[Authorize(Policy = Permissoes.Administracao.Utilizadores)]
public class EditarModel(ServicoPerfis perfis) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public string? Id { get; set; }
    [BindProperty] public string Nome { get; set; } = "";
    [BindProperty] public string? Descricao { get; set; }
    [BindProperty] public decimal? DescontoMaximoPct { get; set; }
    [BindProperty] public List<string> Seleccionadas { get; set; } = [];

    public bool Novo => Id is null;
    public bool Sistema { get; private set; }
    public bool Administrador => Nome == PerfisPadrao.Administrador && Sistema;

    public async Task<IActionResult> OnGetAsync()
    {
        if (Novo) return Page();
        var r = await perfis.ObterAsync(Id!);
        if (r is null) return NotFound();
        var (perfil, permissoes) = r.Value;
        Nome = perfil.Name!;
        Descricao = perfil.Descricao;
        DescontoMaximoPct = perfil.DescontoMaximoPct;
        Sistema = perfil.Sistema;
        Seleccionadas = [.. permissoes];
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var dados = new DadosPerfil(Nome ?? "", Descricao, DescontoMaximoPct, Seleccionadas);
        var r = Novo ? await perfis.CriarAsync(dados) : await perfis.ActualizarAsync(Id!, dados);
        if (r.Sucesso) return RedirecionarComSucesso($"Perfil {Nome} guardado. As sessões abertas recebem as novas permissões no minuto seguinte.", "Index");

        AdicionarErros(r);
        if (!Novo) Sistema = (await perfis.ObterAsync(Id!))?.Perfil.Sistema ?? false;
        return Page();
    }

    public async Task<IActionResult> OnPostApagarAsync()
    {
        var r = await perfis.ApagarAsync(Id!);
        if (r.Sucesso) return RedirecionarComSucesso($"Perfil apagado.", "Index");
        Erro(r.Erros[0]);
        return RedirectToPage(new { Id });
    }
}
