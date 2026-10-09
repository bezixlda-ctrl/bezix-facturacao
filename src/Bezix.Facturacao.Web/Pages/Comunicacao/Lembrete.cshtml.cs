using Bezix.Facturacao.Application.Automacao;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Comunicacao;

/// <summary>Envia já um lembrete de pagamento de um documento (botão no painel e no detalhe do documento).</summary>
[Authorize(Policy = Permissoes.Comunicacao.Enviar)]
public class LembreteModel(ServicoLembretes lembretes) : PaginaBase
{
    public IActionResult OnGet(int id) => RedirectToPage("/Documentos/Detalhe", new { id });

    public async Task<IActionResult> OnPostAsync(int id, string? voltar)
    {
        var r = await lembretes.EnviarAgoraAsync(id);
        if (r.Sucesso) Sucesso("Lembrete enviado ao cliente.");
        else Erro(string.Join(" ", r.Erros));
        return voltar is not null && Url.IsLocalUrl(voltar) ? LocalRedirect(voltar) : RedirectToPage("/Index");
    }
}
