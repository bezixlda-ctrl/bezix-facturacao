using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Recibos;

/// <summary>Pagamentos que os clientes comunicaram pelo link do documento: conferir o comprovativo e emitir o recibo, ou rejeitar.</summary>
[Authorize(Policy = Permissoes.Recibos.ValidarPagamentoComunicado)]
public class ComunicadosModel(ServicoPagamentosComunicados comunicados) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public bool Todos { get; set; }

    public List<PagamentoComunicadoVista> Lista { get; private set; } = [];

    public async Task OnGetAsync() => Lista = await comunicados.ListarAsync(!Todos);

    public async Task<IActionResult> OnGetComprovativoAsync(int id)
    {
        var c = await comunicados.ComprovativoAsync(id);
        if (c is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(c.Value.Conteudo, c.Value.Tipo);
    }

    public async Task<IActionResult> OnPostRejeitarAsync(int id, string? motivo)
    {
        var r = await comunicados.RejeitarAsync(id, motivo);
        if (r.Sucesso) Sucesso("Pagamento rejeitado.");
        else Erro(string.Join(" ", r.Erros));
        return RedirectToPage(new { Todos = Todos ? true : (bool?)null });
    }
}
