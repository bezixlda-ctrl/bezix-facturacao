using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Bezix.Facturacao.Web.Formatacao;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Web.Pages;

public class IndexModel(
    ServicoEmpresa empresas,
    ServicoUtilizadores utilizadores,
    UserManager<Utilizador> gestorUtilizadores,
    AppDbContext db,
    TimeProvider relogio) : PaginaBase
{
    public sealed record Passo(string Titulo, string Explicacao, bool Feito, string? Pagina, string? Accao);

    public string Saudacao { get; private set; } = "";
    public List<Passo> Passos { get; } = [];
    public int PassosFeitos => Passos.Count(p => p.Feito);

    public async Task OnGetAsync()
    {
        var hora = Formato.ParaMaputo(relogio.GetUtcNow().UtcDateTime).Hour;
        var primeiroNome = User.NomeVisivel().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        Saudacao = (hora < 12 ? "Bom dia" : hora < 19 ? "Boa tarde" : "Boa noite") + (primeiroNome.Length > 0 ? $", {primeiroNome}" : "");

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
}
