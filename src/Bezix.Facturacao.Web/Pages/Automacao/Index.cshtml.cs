using Bezix.Facturacao.Application.Automacao;
using Bezix.Facturacao.Domain.Automacao;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Automacao;

/// <summary>Plano de lembretes de pagamento, facturas recorrentes e histórico das tarefas automáticas.</summary>
[Authorize(Policy = Permissoes.Automacao.Ver)]
public class IndexModel(ServicoLembretes lembretes, ServicoRecorrencias recorrencias, ServicoTarefas tarefas) : PaginaBase
{
    [BindProperty] public bool Activo { get; set; }
    [BindProperty] public int DiasAntes { get; set; }
    [BindProperty] public string DiasDepois { get; set; } = "";
    [BindProperty] public int MaxAvisos { get; set; }
    [BindProperty] public int HoraEnvio { get; set; }
    [BindProperty] public bool SoDiasUteis { get; set; }

    public List<RecorrenciaVista> Recorrencias { get; private set; } = [];
    public List<ExecucaoTarefa> Historico { get; private set; } = [];
    public bool PodeGerir => User.TemPermissao(Permissoes.Automacao.Gerir);

    public async Task OnGetAsync()
    {
        var p = await lembretes.PlanoAsync();
        Activo = p.Activo;
        DiasAntes = p.DiasAntes;
        DiasDepois = p.DiasDepois.Replace(",", ", ");
        MaxAvisos = p.MaxAvisos;
        HoraEnvio = p.HoraEnvio;
        SoDiasUteis = p.SoDiasUteis;
        await CarregarAsync();
    }

    public async Task<IActionResult> OnPostLembretesAsync()
    {
        var r = await lembretes.GuardarPlanoAsync(new DadosPlanoLembretes(Activo, DiasAntes, DiasDepois, MaxAvisos, HoraEnvio, SoDiasUteis));
        if (r.Sucesso) return RedirecionarComSucesso("Plano de lembretes guardado.");
        AdicionarErros(r);
        await CarregarAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostExecutarAsync()
    {
        if (!PodeGerir) return Forbid();
        var resultados = await tarefas.ExecutarAsync();
        var falhas = resultados.Count(r => !r.Sucesso);
        if (falhas == 0) Sucesso("Tarefas executadas: " + string.Join("; ", resultados.Select(r => $"{r.Tarefa} — {r.Resultado}")) + ".");
        else Erro($"{falhas} tarefa(s) falharam. Veja o histórico abaixo.");
        return RedirectToPage();
    }

    private async Task CarregarAsync()
    {
        Recorrencias = await recorrencias.ListarAsync();
        Historico = await tarefas.HistoricoAsync(30);
    }

    public static string Periodicidade(Periodicidade p) => p switch
    {
        Domain.Automacao.Periodicidade.Semanal => "Semanal",
        Domain.Automacao.Periodicidade.Mensal => "Mensal",
        Domain.Automacao.Periodicidade.Trimestral => "Trimestral",
        _ => "Anual",
    };
}
