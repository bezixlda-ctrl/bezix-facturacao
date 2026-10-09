using Bezix.Facturacao.Domain.Automacao;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Backups;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bezix.Facturacao.Web.Pages.Administracao;

[Authorize(Policy = Permissoes.Configuracoes.Gerir)]
public class BackupsModel(ServicoBackups backups, IOptions<OpcoesBackups> opcoes, IDestinoBackup destino, AppDbContext db,
    ILogger<BackupsModel> log) : PaginaBase
{
    public OpcoesBackups Opcoes => opcoes.Value;
    public string Destino => destino.Descricao;
    public List<ExecucaoTarefa> Execucoes { get; private set; } = [];
    public List<ItemBackup> Remotos { get; private set; } = [];
    public string? ErroDestino { get; private set; }
    public VerificacaoBackup? Verificacao { get; private set; }

    public async Task OnGetAsync() => await CarregarAsync();

    public async Task<IActionResult> OnPostExecutarAsync()
    {
        try
        {
            var r = await backups.ExecutarAsync(forcar: true, HttpContext.RequestAborted);
            if (r.Executado) Sucesso("Backup feito: " + r.Mensagem);
            else Erro(r.Mensagem);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Erro("O backup falhou: " + ex.Message);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostVerificarAsync(string nome)
    {
        Verificacao = await backups.VerificarAsync(nome, HttpContext.RequestAborted);
        await CarregarAsync();
        return Page();
    }

    private async Task CarregarAsync()
    {
        Execucoes = await db.ExecucoesTarefas.AsNoTracking().Where(e => e.Tarefa == ServicoBackups.Tarefa)
            .OrderByDescending(e => e.Id).Take(15).ToListAsync();
        if (!Opcoes.Configurado) return;
        try
        {
            Remotos = (await backups.ListarAsync(HttpContext.RequestAborted)).OrderByDescending(r => r.Nome).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Não foi possível listar os backups em {Destino}.", destino.Descricao);
            ErroDestino = ex.Message;
        }
    }
}
