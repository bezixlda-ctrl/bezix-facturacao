using Bezix.Facturacao.Application.Fiscal;
using Bezix.Facturacao.Domain.Fiscal;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Relatorios;

[Authorize(Policy = Permissoes.Relatorios.Fiscal)]
public class ExportacaoATModel(ServicoExportacaoAT exportacao) : PaginaBase
{
    [BindProperty] public int Ano { get; set; }
    [BindProperty] public int Mes { get; set; }

    public List<ExportacaoAT> Historico { get; private set; } = [];
    public List<EstadoSerieIntegridade>? Integridade { get; private set; }
    public DateOnly Hoje => exportacao.Hoje();

    public async Task OnGetAsync()
    {
        var anterior = new DateOnly(Hoje.Year, Hoje.Month, 1).AddMonths(-1);
        Ano = anterior.Year;
        Mes = anterior.Month;
        Historico = await exportacao.ListarAsync();
    }

    public async Task<IActionResult> OnPostGerarAsync()
    {
        var (r, _) = await exportacao.GerarAsync(Ano, Mes);
        if (r.Sucesso) return RedirecionarComSucesso($"Ficheiro de {Mes:00}/{Ano} gerado.");
        AdicionarErros(r);
        Historico = await exportacao.ListarAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetFicheiroAsync(int id)
    {
        var f = await exportacao.FicheiroAsync(id);
        if (f is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return File(f.Value.Conteudo, "application/zip", f.Value.Nome);
    }

    public async Task<IActionResult> OnPostSubmetidaAsync(int id, string? referencia)
    {
        var r = await exportacao.MarcarSubmetidaAsync(id, referencia);
        if (r.Sucesso) Sucesso("Exportação marcada como submetida no e-Declaração.");
        else Erro(string.Join(" ", r.Erros));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostIntegridadeAsync()
    {
        await OnGetAsync();
        Integridade = await exportacao.VerificarIntegridadeAsync();
        return Page();
    }

    public static string NomeMes(int mes) => new DateOnly(2000, mes, 1).ToString("MMMM", Formatacao.Formato.Cultura);
}
