using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Bezix.Facturacao.Web.Formatacao;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Clientes;

[Authorize(Policy = Permissoes.Clientes.Ver)]
public class DetalheModel(ServicoClientes clientes, ServicoEmpresa empresas, ServicoUtilizadores utilizadores,
    Application.Documentos.ServicoDocumentos documentos, Application.Recebimentos.ServicoContaCorrente contaCorrente, TimeProvider relogio) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public Cliente Cliente { get; private set; } = null!;
    public List<Actividade> Actividades { get; private set; } = [];
    public Application.Comum.Pagina<Application.Documentos.DocumentoResumo>? Documentos { get; private set; }
    public bool PodeFacturar => documentos.PodeEditar(Domain.Documentos.TipoDocumento.FT);
    public Application.Recebimentos.SaldoCliente? Saldo { get; private set; }
    public int PrazoEfectivo { get; private set; }
    public string? NomeResponsavel { get; private set; }
    public bool PodeGerir => User.TemPermissao(Permissoes.Clientes.Gerir);

    // Nova actividade
    [BindProperty] public TipoActividade NovoTipo { get; set; } = TipoActividade.Chamada;
    [BindProperty] public DateTime? NovaData { get; set; }
    [BindProperty] public string? NovaNota { get; set; }

    public DateTime AgoraMaputo => Formato.ParaMaputo(relogio.GetUtcNow().UtcDateTime);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CarregarAsync()) return NotFound();
        if (User.TemPermissao(Permissoes.Documentos.Ver))
            Saldo = await contaCorrente.SaldoAsync(Id);
        if (User.TemPermissao(Permissoes.Documentos.Ver))
            Documentos = await documentos.PesquisarAsync(new Application.Documentos.FiltroDocumentos(ClienteId: Id, Tamanho: 10));
        return Page();
    }

    public async Task<IActionResult> OnPostActividadeAsync()
    {
        if (!PodeGerir) return Forbid();
        if (!await CarregarAsync()) return NotFound();
        var local = NovaData ?? AgoraMaputo;
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Formato.FusoMaputo);
        var r = await clientes.AdicionarActividadeAsync(Id, NovoTipo, utc, NovaNota ?? "", User.NomeVisivel());
        if (r.Sucesso)
        {
            NovaNota = null;
            NovaData = null;
            ModelState.Clear();
        }
        else AdicionarErros(r);

        Actividades = await clientes.ActividadesAsync(Id);
        return Partial("_Actividades", this);
    }

    public async Task<IActionResult> OnPostConcluirAsync(int actividadeId)
    {
        if (!PodeGerir) return Forbid();
        if (!await CarregarAsync()) return NotFound();
        await clientes.ConcluirTarefaAsync(actividadeId);
        Actividades = await clientes.ActividadesAsync(Id);
        return Partial("_Actividades", this);
    }

    public async Task<IActionResult> OnPostEstadoAsync(bool activo)
    {
        if (!PodeGerir) return Forbid();
        var r = await clientes.DefinirActivoAsync(Id, activo);
        if (!r.Sucesso) return NotFound();
        return RedirecionarComSucesso(activo ? "Cliente reactivado." : "Cliente desactivado. Deixa de aparecer nas listas e em documentos novos.", null, new { Id });
    }

    private async Task<bool> CarregarAsync()
    {
        var c = await clientes.ObterAsync(Id);
        if (c is null) return false;
        Cliente = c;
        Actividades = await clientes.ActividadesAsync(Id);
        PrazoEfectivo = c.PrazoPagamentoDias ?? (await empresas.ObterAsync()).PrazoPagamentoDias;
        if (c.ResponsavelId is not null)
            NomeResponsavel = (await utilizadores.ObterAsync(c.ResponsavelId))?.Utilizador.Nome;
        return true;
    }

    public static string Icone(TipoActividade t) => t switch
    {
        TipoActividade.Chamada => "bi-telephone",
        TipoActividade.Reuniao => "bi-people",
        TipoActividade.Tarefa => "bi-check2-square",
        _ => "bi-sticky",
    };

    public static string Nome(TipoActividade t) => t switch
    {
        TipoActividade.Chamada => "Chamada",
        TipoActividade.Reuniao => "Reunião",
        TipoActividade.Tarefa => "Tarefa",
        _ => "Nota",
    };
}
