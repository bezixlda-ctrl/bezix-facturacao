using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Application.Painel;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Bezix.Facturacao.Web.Formatacao;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Web.Pages;

/// <summary>
/// Painel: em 5 segundos, quanto se facturou, quanto se recebeu, quem deve e o que fazer hoje.
/// O conteúdo depende das permissões Dashboard.* do perfil; os gráficos carregam depois da página.
/// </summary>
public class IndexModel(
    ServicoEmpresa empresas,
    ServicoUtilizadores utilizadores,
    UserManager<Utilizador> gestorUtilizadores,
    ServicoPainel painel,
    ServicoEmissao emissao,
    Application.Recebimentos.ServicoPagamentosComunicados comunicados,
    Application.Automacao.ServicoRecorrencias recorrencias,
    Application.Integracoes.ServicoWebhooks webhooks,
    AppDbContext db,
    TimeProvider relogio) : PaginaBase
{
    public sealed record Passo(string Titulo, string Explicacao, bool Feito, string? Pagina, string? Accao);

    public string Saudacao { get; private set; } = "";
    public List<Passo> Passos { get; } = [];
    public int PassosFeitos => Passos.Count(p => p.Feito);

    public Indicadores? Indicadores { get; private set; }
    public List<DocumentoAccao> Vencidas { get; private set; } = [];
    public List<DocumentoAccao> AVencer { get; private set; } = [];
    public List<DocumentoResumo> Rascunhos { get; private set; } = [];
    public List<DocumentoResumo> UltimosDocumentos { get; private set; } = [];
    public List<DocumentoResumo> UltimosRecebimentos { get; private set; } = [];
    public List<ClienteValor> TopClientes { get; private set; } = [];
    public List<ClienteValor> TopDevedores { get; private set; } = [];
    public List<Domain.Auditoria.RegistoAuditoria> Actividade { get; private set; } = [];
    public int AprovacoesPendentes { get; private set; }
    public int EmailsFalhados { get; private set; }
    public int PagamentosPorValidar { get; private set; }
    public List<Application.Automacao.RecorrenciaVista> RecorrentesSemana { get; private set; } = [];
    public int WebhooksFalhados { get; private set; }
    public List<Serie> SeriesPorFechar { get; private set; } = [];
    public Dictionary<int, Application.Recebimentos.SituacaoDocumento> Situacao { get; private set; } = [];

    public bool VeFacturacao => User.TemAlguma(Permissoes.Dashboard.Facturacao, Permissoes.Dashboard.Resumo);
    public bool VeRecebimentos => User.TemPermissao(Permissoes.Dashboard.Recebimentos);
    public bool VeSaldos => User.TemPermissao(Permissoes.Dashboard.Saldos);
    public bool VeIva => User.TemPermissao(Permissoes.Dashboard.Iva);
    public bool VeGestao => User.TemPermissao(Permissoes.Dashboard.Gestao);
    public bool VeGraficos => VeFacturacao || VeRecebimentos;

    public async Task OnGetAsync()
    {
        var hora = Formato.ParaMaputo(relogio.GetUtcNow().UtcDateTime).Hour;
        var primeiroNome = User.NomeVisivel().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        Saudacao = (hora < 12 ? "Bom dia" : hora < 19 ? "Boa tarde" : "Boa noite") + (primeiroNome.Length > 0 ? $", {primeiroNome}" : "");

        await CarregarPassosAsync();

        if (User.TemAlguma(Permissoes.Dashboard.Facturacao, Permissoes.Dashboard.Recebimentos, Permissoes.Dashboard.Saldos,
                Permissoes.Dashboard.Iva, Permissoes.Dashboard.Gestao, Permissoes.Dashboard.Resumo))
            Indicadores = await painel.IndicadoresAsync();

        if (VeSaldos) (Vencidas, AVencer) = await painel.CobrancasAsync();
        if (User.TemAlguma(Permissoes.Documentos.Rascunho, Permissoes.Documentos.EmitirFactura, Permissoes.Documentos.EmitirFacturaRecibo))
            Rascunhos = await painel.RascunhosAsync();
        if (User.TemPermissao(Permissoes.Documentos.Aprovar)) AprovacoesPendentes = await emissao.ContarPendentesAsync();
        if (User.TemAlguma(Permissoes.Comunicacao.Enviar, Permissoes.Comunicacao.GerirModelos)) EmailsFalhados = await painel.EmailsFalhadosAsync();
        if (User.TemPermissao(Permissoes.Configuracoes.Gerir)) SeriesPorFechar = await painel.SeriesPorFecharAsync();
        if (User.TemPermissao(Permissoes.Recibos.ValidarPagamentoComunicado)) PagamentosPorValidar = await comunicados.ContarPorValidarAsync();
        if (User.TemPermissao(Permissoes.Automacao.Ver)) RecorrentesSemana = await recorrencias.ProximasAsync(7);
        if (User.TemPermissao(Permissoes.Administracao.Integracoes)) WebhooksFalhados = await webhooks.ContarFalhadasAsync();

        if (VeFacturacao)
        {
            UltimosDocumentos = await painel.UltimosAsync([TipoDocumento.FT, TipoDocumento.FR, TipoDocumento.FG, TipoDocumento.NC, TipoDocumento.ND], 8);
            Situacao = await HttpContext.RequestServices.GetRequiredService<Application.Recebimentos.ServicoContaCorrente>()
                .SituacaoAsync(UltimosDocumentos.Select(d => d.Id).ToList());
        }
        if (VeRecebimentos) UltimosRecebimentos = await painel.UltimosAsync([TipoDocumento.RC, TipoDocumento.FR], 5);
        if (VeGestao) TopClientes = await painel.TopClientesAsync();
        if (VeSaldos) TopDevedores = await painel.TopDevedoresAsync();
        if (User.TemAlguma(Permissoes.Dashboard.Facturacao, Permissoes.Dashboard.Recebimentos, Permissoes.Dashboard.Gestao))
            Actividade = await painel.ActividadeRecenteAsync();
    }

    /// <summary>Dados dos gráficos (pedidos depois de a página aparecer).</summary>
    public async Task<IActionResult> OnGetGraficosAsync(PeriodoSerie periodo = PeriodoSerie.Mensal)
    {
        if (!VeGraficos && !VeSaldos) return Forbid();
        var serie = VeGraficos ? await painel.SerieAsync(periodo) : [];
        var antiguidade = VeSaldos ? await painel.AntiguidadeAsync() : [];
        var mensal = VeGraficos && periodo != PeriodoSerie.Mensal ? await painel.SerieAsync(PeriodoSerie.Mensal) : serie;
        return new JsonResult(new
        {
            rotulos = serie.Select(p => p.Rotulo),
            facturado = VeFacturacao ? serie.Select(p => p.Facturado) : null,
            recebido = VeRecebimentos ? serie.Select(p => p.Recebido) : null,
            sparkFacturado = mensal.Select(p => p.Facturado),
            sparkRecebido = mensal.Select(p => p.Recebido),
            antiguidade = VeSaldos ? new[]
            {
                antiguidade.Sum(l => l.Ate30), antiguidade.Sum(l => l.De31a60), antiguidade.Sum(l => l.De61a90), antiguidade.Sum(l => l.Mais90),
            } : null,
        });
    }

    private async Task CarregarPassosAsync()
    {
        var eu = await gestorUtilizadores.GetUserAsync(User);

        if (User.TemPermissao(Permissoes.Configuracoes.Gerir))
        {
            var empresa = await empresas.ObterAsync();
            var ano = Formato.HojeEmMaputo(relogio).Year;

            Passos.Add(new("Preencher os dados fiscais da empresa",
                "Nome, NUIT e endereço aparecem em todas as facturas.",
                empresa.Nuit != InicializadorBaseDados.NuitPorPreencher && empresa.Endereco != "Por preencher",
                "/Configuracoes/Empresa", "Preencher dados"));
            Passos.Add(new("Carregar o logótipo",
                "PNG ou JPG até 2 MB. Aparece no topo dos documentos e neste menu.",
                empresa.LogoPath is not null, "/Configuracoes/Empresa", "Carregar logótipo"));
            Passos.Add(new("Indicar as contas bancárias",
                "As contas marcadas aparecem no rodapé das facturas para o cliente pagar.",
                await db.ContasBancarias.AnyAsync(c => c.MostrarNoDocumento), "/Configuracoes/ContasBancarias/Index", "Adicionar conta"));
            Passos.Add(new($"Confirmar as séries de {ano}",
                "Cada tipo de documento precisa de uma série activa no ano para poder ser emitido.",
                await db.Series.AnyAsync(s => s.Ano == ano && s.Estado == EstadoSerie.Activa && s.TipoDoc == TipoDocumento.FT),
                "/Configuracoes/Series", "Ver séries"));
        }

        if (User.TemPermissao(Permissoes.Administracao.Utilizadores))
        {
            var admins = await utilizadores.ContarAdministradoresActivosAsync();
            Passos.Add(new("Criar um segundo administrador",
                $"Com {ServicoUtilizadores.MinimoAdministradores} administradores activos, a empresa não fica sem acesso se um faltar.",
                admins >= ServicoUtilizadores.MinimoAdministradores, "/Administracao/Utilizadores/Editar", "Criar utilizador"));
        }

        if (eu is not null && await gestorUtilizadores.IsInRoleAsync(eu, PerfisPadrao.Administrador))
        {
            Passos.Add(new("Activar a verificação em dois passos",
                "Os administradores devem confirmar a entrada com um código do telemóvel.",
                eu.TwoFactorEnabled, "/Conta/Seguranca", "Activar"));
        }
    }

    /// <summary>Texto amigável para uma entrada da auditoria na linha do tempo.</summary>
    public static (string Icone, string Texto) DescreverActividade(Domain.Auditoria.RegistoAuditoria a)
    {
        string? Campo(string nome)
        {
            if (a.Depois is null) return null;
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(a.Depois);
                return json.RootElement.TryGetProperty(nome, out var v) ? v.ToString() : null;
            }
            catch (System.Text.Json.JsonException) { return null; }
        }
        return a.Accao switch
        {
            "Documento.Emitir" => ("bi-file-earmark-check", $"{Campo("NumeroFormatado")} emitida para {Campo("ClienteNome")}"),
            "Recibo.Emitir" => ("bi-cash-coin", $"Pagamento recebido de {Campo("Nome")} ({Campo("NumeroFormatado")})"),
            "Documento.Anular" => ("bi-x-octagon", $"{Campo("NumeroFormatado")} anulado"),
            "Documento.PedirAprovacao" => ("bi-patch-question", "Aprovação pedida"),
            "Documento.Enviar" => ("bi-envelope", $"{Campo("NumeroFormatado")} enviado para {Campo("Para")}"),
            "Documento.Visto" => ("bi-eye", $"{Campo("NumeroFormatado")} visto pelo cliente"),
            "Pagamento.Comunicado" => ("bi-bell", $"Pagamento comunicado por {Campo("Cliente")}"),
            "Cliente.Criar" => ("bi-person-plus", $"Novo cliente: {Campo("Nome")}"),
            _ => ("bi-dot", a.Accao),
        };
    }
}
