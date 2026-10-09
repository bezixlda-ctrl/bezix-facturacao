using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Administracao.Utilizadores;

[Authorize(Policy = Permissoes.Administracao.Utilizadores)]
public class EditarModel(ServicoUtilizadores utilizadores, ServicoPerfis perfis) : PaginaBase
{
    public sealed class Formulario
    {
        public string Nome { get; set; } = "";
        public string Email { get; set; } = "";
        public string Perfil { get; set; } = PerfisPadrao.Facturador;
        public bool Activo { get; set; } = true;
        public string? Password { get; set; }
    }

    [BindProperty(SupportsGet = true)] public string? Id { get; set; }
    [BindProperty] public Formulario Dados { get; set; } = new();
    [BindProperty] public string? NovaPassword { get; set; }

    public List<PerfilResumo> Perfis { get; private set; } = [];
    public bool Novo => Id is null;
    public bool Bloqueado { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Perfis = await perfis.ListarAsync();
        if (Novo) return Page();

        var r = await utilizadores.ObterAsync(Id!);
        if (r is null) return NotFound();
        var (u, perfil) = r.Value;
        Dados = new Formulario { Nome = u.Nome, Email = u.Email ?? "", Perfil = perfil ?? "", Activo = u.Activo };
        Bloqueado = u.Activo && u.LockoutEnd > DateTimeOffset.UtcNow;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var dados = new DadosUtilizador(Dados.Nome ?? "", Dados.Email ?? "", Dados.Perfil ?? "", Dados.Activo);
        var r = Novo
            ? await utilizadores.CriarAsync(dados, Dados.Password ?? "")
            : await utilizadores.ActualizarAsync(Id!, dados);
        if (r.Sucesso)
            return RedirecionarComSucesso(Novo ? $"Utilizador {dados.Nome} criado. Entregue-lhe a password inicial por um canal seguro." : "Utilizador guardado.", "Index");

        AdicionarErros(r);
        Perfis = await perfis.ListarAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        if (Novo) return NotFound();
        var r = await utilizadores.RedefinirPasswordAsync(Id!, NovaPassword ?? "");
        if (r.Sucesso) return RedirecionarComSucesso("Password redefinida e conta desbloqueada.", null, new { Id });

        AdicionarErros(r);
        await OnGetAsync();
        return Page();
    }
}
