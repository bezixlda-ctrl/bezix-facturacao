using Bezix.Facturacao.Application.Integracoes;
using Bezix.Facturacao.Domain.Integracoes;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Administracao.Integracoes;

/// <summary>Dados do partial _Entregas: lista, se mostra a coluna do destino e a página que trata o "Reenviar".</summary>
public sealed record VistaEntregas(IReadOnlyList<WebhookEntrega> Entregas, bool MostrarDestino, string Pagina);

[Authorize(Policy = Permissoes.Administracao.Integracoes)]
public class WebhookModel(ServicoWebhooks webhooks) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty] public string Nome { get; set; } = "";
    [BindProperty] public string Url { get; set; } = "";
    [BindProperty] public List<string> Eventos { get; set; } = [];
    [BindProperty] public bool Activa { get; set; }

    public WebhookSubscricao Subscricao { get; private set; } = null!;
    public List<WebhookEntrega> Entregas { get; private set; } = [];
    public string? SegredoNovo { get; private set; }
    public VistaEntregas VistaEntregas => new(Entregas, false, "Webhook");

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CarregarAsync()) return NotFound();
        Nome = Subscricao.Nome;
        Url = Subscricao.Url;
        Eventos = [.. Subscricao.Eventos.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
        Activa = Subscricao.Activa;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var r = await webhooks.ActualizarAsync(Id, Nome, Url, Eventos, Activa);
        if (r.Sucesso) return RedirecionarComSucesso("Webhook guardado.", null, new { Id });
        AdicionarErros(r);
        if (!await CarregarAsync()) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostTestarAsync()
    {
        var r = await webhooks.TestarAsync(Id);
        if (r.Sucesso) Sucesso("O destino recebeu o evento de teste.");
        else Erro(string.Join(" ", r.Erros));
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostSegredoAsync()
    {
        var (r, segredo) = await webhooks.NovoSegredoAsync(Id);
        if (!r.Sucesso) return NotFound();
        SegredoNovo = segredo;
        await OnGetAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostReenviarAsync(long entrega)
    {
        var r = await webhooks.ReenviarAsync(entrega);
        if (r.Sucesso) Sucesso("Entrega repetida. Veja o novo estado na lista.");
        else Erro(string.Join(" ", r.Erros));
        return RedirectToPage(new { Id });
    }

    private async Task<bool> CarregarAsync()
    {
        var s = (await webhooks.SubscricoesAsync()).FirstOrDefault(x => x.Id == Id);
        if (s is null) return false;
        Subscricao = s;
        Entregas = (await webhooks.EntregasAsync(Id, null, 1)).Itens.ToList();
        return true;
    }
}
