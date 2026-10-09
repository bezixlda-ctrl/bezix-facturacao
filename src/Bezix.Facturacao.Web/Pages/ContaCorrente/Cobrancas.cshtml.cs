using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.ContaCorrente;

/// <summary>Clientes com facturas vencidas: enviar o extracto de conta corrente a vários de uma vez.</summary>
[Authorize(Policy = Permissoes.Comunicacao.Enviar)]
public class CobrancasModel(ServicoContaCorrente contaCorrente, ServicoComunicacao comunicacao) : PaginaBase
{
    [BindProperty(SupportsGet = true, Name = "p")] public int NumeroPagina { get; set; } = 1;
    [BindProperty] public List<int> Seleccionados { get; set; } = [];

    public Pagina<ClienteSaldoLinha> Resultado { get; private set; } = new([], 0, 1, Pagina<ClienteSaldoLinha>.TamanhoPadrao);
    public DateOnly Hoje { get; private set; }

    public async Task OnGetAsync()
    {
        Hoje = contaCorrente.Hoje();
        Resultado = await contaCorrente.SaldosAsync(null, soComSaldo: true, soVencidos: true, NumeroPagina);
    }

    public async Task<IActionResult> OnPostEnviarAsync()
    {
        if (Seleccionados.Count == 0)
        {
            Erro("Escolha pelo menos um cliente.");
            return RedirectToPage(new { p = NumeroPagina });
        }
        // Extracto dos últimos 12 meses: o saldo anterior aparece como saldo inicial.
        var ate = contaCorrente.Hoje();
        var de = new DateOnly(ate.Year, ate.Month, 1).AddMonths(-11);
        int enviados = 0;
        var falhas = new List<string>();
        foreach (var id in Seleccionados.Distinct().Take(100))
        {
            var email = await comunicacao.PrepararExtractoAsync(id, de, ate);
            if (email is null) continue;
            if (string.IsNullOrWhiteSpace(email.Para)) { falhas.Add($"cliente {id} sem email"); continue; }
            var r = await comunicacao.EnviarExtractoAsync(id, de, ate, email);
            if (r.Sucesso) enviados++;
            else falhas.Add($"{email.Para}: {string.Join(" ", r.Erros)}");
        }
        if (enviados > 0) Sucesso($"{enviados} extracto(s) enviado(s).");
        if (falhas.Count > 0) Erro("Não enviados — " + string.Join("; ", falhas.Take(5)) + (falhas.Count > 5 ? $" e mais {falhas.Count - 5}." : "."));
        return RedirectToPage(new { p = NumeroPagina == 1 ? (int?)null : NumeroPagina });
    }

    public Paginacao Paginacao => Paginacao.De(Resultado,
        p => Url.Page("/ContaCorrente/Cobrancas", new { p = p == 1 ? (int?)null : p })!, null);
}
