using Bezix.Facturacao.Domain.Auditoria;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Web.Pages.Administracao;

[Authorize(Policy = Permissoes.Administracao.VerAuditoria)]
public class AuditoriaModel(AppDbContext db) : PaginaBase
{
    public const int TamanhoPagina = 50;

    [BindProperty(SupportsGet = true)] public string? Pesquisa { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public List<RegistoAuditoria> Registos { get; private set; } = [];
    public bool HaMais { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Pagina < 1) Pagina = 1;
        var q = db.Auditoria.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Pesquisa))
        {
            var p = Pesquisa.Trim();
            q = q.Where(r => r.Accao.Contains(p) || r.Entidade.Contains(p) || (r.Utilizador != null && r.Utilizador.Contains(p)) || r.EntidadeId == p);
        }

        var pagina = await q.OrderByDescending(r => r.Id)
            .Skip((Pagina - 1) * TamanhoPagina)
            .Take(TamanhoPagina + 1)
            .ToListAsync();
        HaMais = pagina.Count > TamanhoPagina;
        Registos = pagina.Take(TamanhoPagina).ToList();

        // Pedido htmx (botão "Mostrar mais"): devolve só as linhas novas.
        return Request.Headers.ContainsKey("HX-Request") ? Partial("_LinhasAuditoria", this) : Page();
    }
}
