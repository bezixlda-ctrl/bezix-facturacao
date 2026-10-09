using Bezix.Facturacao.Application.Relatorios;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Formatacao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Relatorios;

[Authorize(Policy = Permissoes.Relatorios.Vendas)]
public class VendasModel(ServicoRelatorios relatorios, TimeProvider relogio) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public DateOnly? De { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Ate { get; set; }
    [BindProperty(SupportsGet = true)] public AgruparVendas Agrupar { get; set; } = AgruparVendas.Cliente;

    public List<LinhaVendas> Linhas { get; private set; } = [];
    public bool SoProprios => relatorios.SoProprios;
    public DateOnly Inicio => De ?? new DateOnly(Hoje.Year, Hoje.Month, 1);
    public DateOnly Fim => Ate ?? Hoje;
    private DateOnly Hoje => DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime.AddHours(2));

    public async Task OnGetAsync() => Linhas = await relatorios.VendasAsync(Inicio, Fim, Agrupar);

    public async Task<IActionResult> OnGetCsvAsync()
    {
        Linhas = await relatorios.VendasAsync(Inicio, Fim, Agrupar);
        var artigo = Agrupar == AgruparVendas.Artigo;
        var csv = artigo
            ? new CsvExcel(Coluna, "Descrição", "Documentos", "Quantidade", "Base", "IVA", "Total")
            : new CsvExcel(Coluna, "Descrição", "Documentos", "Base", "IVA", "Total");
        foreach (var l in Linhas)
        {
            if (artigo) csv.Linha(l.Chave, l.Descricao, l.Documentos, l.Quantidade, l.Base, l.Iva, l.Total);
            else csv.Linha(l.Chave, l.Descricao, l.Documentos, l.Base, l.Iva, l.Total);
        }
        return csv.Ficheiro($"vendas-{Agrupar.ToString().ToLowerInvariant()}-{Inicio:yyyyMMdd}-{Fim:yyyyMMdd}.csv");
    }

    public string Coluna => Agrupar switch
    {
        AgruparVendas.Mes => "Mês",
        AgruparVendas.Cliente => "NUIT",
        AgruparVendas.Artigo => "Referência",
        _ => "Tipo",
    };
}
