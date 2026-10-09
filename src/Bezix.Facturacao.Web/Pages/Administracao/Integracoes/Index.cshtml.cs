using Bezix.Facturacao.Application.Integracoes;
using Bezix.Facturacao.Domain.Integracoes;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Administracao.Integracoes;

/// <summary>Chaves da API v1 e subscrições de webhooks. Chaves e segredos só são mostrados uma vez, logo após serem criados.</summary>
[Authorize(Policy = Permissoes.Administracao.Integracoes)]
public class IndexModel(ServicoChavesApi chaves, ServicoWebhooks webhooks) : PaginaBase
{
    public List<ChaveApi> Chaves { get; private set; } = [];
    public List<WebhookSubscricao> Webhooks { get; private set; } = [];
    public int EntregasFalhadas { get; private set; }

    /// <summary>Chave ou segredo acabado de criar (mostrado só nesta resposta, nunca guardado em claro).</summary>
    public string? SegredoNovo { get; private set; }
    public string? SegredoDescricao { get; private set; }

    [BindProperty] public string? NomeChave { get; set; }
    [BindProperty] public List<string> Escopos { get; set; } = [];
    [BindProperty] public string? NomeWebhook { get; set; }
    [BindProperty] public string? UrlWebhook { get; set; }
    [BindProperty] public List<string> Eventos { get; set; } = [];

    public async Task OnGetAsync() => await CarregarAsync();

    public async Task<IActionResult> OnPostChaveAsync()
    {
        var (r, chave) = await chaves.CriarAsync(NomeChave ?? "", Escopos);
        if (r.Sucesso)
        {
            SegredoNovo = chave;
            SegredoDescricao = $"Chave “{NomeChave}” criada. Copie-a agora: não volta a ser mostrada.";
            NomeChave = null;
            Escopos = [];
        }
        else AdicionarErros(r);
        await CarregarAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRevogarAsync(int id)
    {
        var r = await chaves.RevogarAsync(id);
        if (r.Sucesso) Sucesso("Chave revogada. Os pedidos com esta chave passam a ser recusados.");
        else Erro(string.Join(" ", r.Erros));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostWebhookAsync()
    {
        var (r, segredo) = await webhooks.CriarAsync(NomeWebhook ?? "", UrlWebhook ?? "", Eventos);
        if (r.Sucesso)
        {
            SegredoNovo = segredo;
            SegredoDescricao = $"Webhook “{NomeWebhook}” criado. Guarde o segredo de assinatura no sistema de destino: não volta a ser mostrado.";
            NomeWebhook = UrlWebhook = null;
            Eventos = [];
        }
        else AdicionarErros(r);
        await CarregarAsync();
        return Page();
    }

    private async Task CarregarAsync()
    {
        Chaves = await chaves.ListarAsync();
        Webhooks = await webhooks.SubscricoesAsync();
        EntregasFalhadas = await webhooks.ContarFalhadasAsync();
    }

    public static string NomeEvento(string e) => e switch
    {
        EventosWebhook.DocumentoEmitido => "Documento emitido",
        EventosWebhook.DocumentoAnulado => "Documento anulado",
        EventosWebhook.DocumentoEnviado => "Documento enviado ao cliente",
        EventosWebhook.PagamentoRecebido => "Pagamento recebido (recibo)",
        EventosWebhook.DocumentoVencido => "Factura vencida",
        EventosWebhook.ClienteCriado => "Cliente criado",
        _ => e,
    };
}
