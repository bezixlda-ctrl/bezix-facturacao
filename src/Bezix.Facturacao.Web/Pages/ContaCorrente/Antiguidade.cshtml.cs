using System.Text;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Formatacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.ContaCorrente;

[Authorize(Policy = Permissoes.Clientes.Ver)]
public class AntiguidadeModel(ServicoContaCorrente contaCorrente) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public DateOnly? Referencia { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public List<LinhaAntiguidade> Linhas { get; private set; } = [];
    public LinhaAntiguidade Total { get; private set; } = new(0, "Total", 0, 0, 0, 0, 0, 0, 0);
    public DateOnly Data => Referencia ?? contaCorrente.Hoje();

    public async Task OnGetAsync() => await CarregarAsync();

    /// <summary>Exportação para Excel (CSV com ";" e vírgula decimal, como o Excel português espera).</summary>
    public async Task<IActionResult> OnGetCsvAsync()
    {
        await CarregarAsync();
        var sb = new StringBuilder("Cliente;A vencer;0-30 dias;31-60 dias;61-90 dias;Mais de 90 dias;Créditos;Saldo\r\n");
        foreach (var l in Linhas.Append(Total))
            sb.Append('"').Append(l.ClienteNome.Replace("\"", "\"\"")).Append('"')
              .Append(';').Append(Formato.Numero(l.AVencer)).Append(';').Append(Formato.Numero(l.Ate30))
              .Append(';').Append(Formato.Numero(l.De31a60)).Append(';').Append(Formato.Numero(l.De61a90))
              .Append(';').Append(Formato.Numero(l.Mais90)).Append(';').Append(Formato.Numero(l.Creditos))
              .Append(';').Append(Formato.Numero(l.Saldo)).Append("\r\n");
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"antiguidade-saldos-{Data:yyyyMMdd}.csv");
    }

    private async Task CarregarAsync()
    {
        Linhas = await contaCorrente.AntiguidadeAsync(Data, Q);
        Total = new LinhaAntiguidade(0, "Total", Linhas.Sum(l => l.AVencer), Linhas.Sum(l => l.Ate30), Linhas.Sum(l => l.De31a60),
            Linhas.Sum(l => l.De61a90), Linhas.Sum(l => l.Mais90), Linhas.Sum(l => l.Creditos), Linhas.Sum(l => l.Saldo));
    }
}
