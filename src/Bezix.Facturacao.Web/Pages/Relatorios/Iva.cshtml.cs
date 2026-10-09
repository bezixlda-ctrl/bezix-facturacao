using Bezix.Facturacao.Application.Relatorios;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Formatacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Relatorios;

[Authorize(Policy = Permissoes.Relatorios.Fiscal)]
public class IvaModel(ServicoRelatorios relatorios, TimeProvider relogio) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public DateOnly? De { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Ate { get; set; }

    public List<LinhaMapaIva> Linhas { get; private set; } = [];
    public List<LinhaAnulado> Anulados { get; private set; } = [];
    public DateOnly Inicio => De ?? new DateOnly(Hoje.Year, Hoje.Month, 1).AddMonths(-1);
    public DateOnly Fim => Ate ?? new DateOnly(Hoje.Year, Hoje.Month, 1).AddDays(-1);
    private DateOnly Hoje => DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime.AddHours(2));

    public async Task OnGetAsync()
    {
        Linhas = await relatorios.MapaIvaAsync(Inicio, Fim);
        Anulados = await relatorios.AnuladosAsync(Inicio, Fim);
    }

    public async Task<IActionResult> OnGetCsvAsync()
    {
        var csv = new CsvExcel("Taxa IVA (%)", "Motivo de isenção", "Base facturada", "IVA facturado", "Base creditada (NC)", "IVA creditado (NC)", "Base líquida", "IVA liquidado");
        foreach (var l in await relatorios.MapaIvaAsync(Inicio, Fim))
            csv.Linha(l.TaxaPct, l.MotivoIsencao, l.BaseFacturada, l.IvaFacturado, l.BaseCreditada, l.IvaCreditado, l.Base, l.Iva);
        return csv.Ficheiro($"mapa-iva-{Inicio:yyyyMMdd}-{Fim:yyyyMMdd}.csv");
    }

    public async Task<IActionResult> OnGetAnuladosCsvAsync()
    {
        var csv = new CsvExcel("Documento", "Data", "Cliente", "Total", "Anulado em", "Anulado por", "Motivo");
        foreach (var a in await relatorios.AnuladosAsync(Inicio, Fim))
            csv.Linha(a.Numero, a.Data, a.Cliente, a.Total, a.AnuladoEm, a.AnuladoPor, a.Motivo);
        return csv.Ficheiro($"anulados-{Inicio:yyyyMMdd}-{Fim:yyyyMMdd}.csv");
    }
}
