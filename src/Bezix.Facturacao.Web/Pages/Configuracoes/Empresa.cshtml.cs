using System.ComponentModel.DataAnnotations;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Configuracoes;

[Authorize(Policy = Permissoes.Configuracoes.Gerir)]
[RequestSizeLimit(3 * 1024 * 1024)]
public class EmpresaModel(ServicoEmpresa empresas) : PaginaBase
{
    public sealed class Formulario
    {
        public string Nome { get; set; } = "";
        public string Nuit { get; set; } = "";
        public string? Slogan { get; set; }
        public string Endereco { get; set; } = "";
        public string? Cidade { get; set; }
        public string? Provincia { get; set; }
        public string? Telefones { get; set; }
        public string? Email { get; set; }
        public string? Site { get; set; }
        public string CorPrimaria { get; set; } = "#0F5C8C";
        public string RodapeTexto { get; set; } = "";
        [Range(0, 365)] public int PrazoPagamentoDias { get; set; }
        public decimal LimiteNuitObrigatorio { get; set; }
        public ModoArredondamentoIva ModoArredondamentoIva { get; set; }
    }

    [BindProperty] public Formulario Dados { get; set; } = new();
    [BindProperty] public IFormFile? Logotipo { get; set; }

    public bool TemLogotipo { get; private set; }
    public long VersaoLogotipo { get; private set; }
    public bool NuitPorPreencher { get; private set; }

    public async Task OnGetAsync()
    {
        var e = await empresas.ObterAsync();
        Dados = new Formulario
        {
            Nome = e.Nome,
            Nuit = e.Nuit == InicializadorBaseDados.NuitPorPreencher ? "" : e.Nuit,
            Slogan = e.Slogan,
            Endereco = e.Endereco == "Por preencher" ? "" : e.Endereco,
            Cidade = e.Cidade,
            Provincia = e.Provincia,
            Telefones = e.Telefones,
            Email = e.Email,
            Site = e.Site,
            CorPrimaria = e.CorPrimaria,
            RodapeTexto = e.RodapeTexto,
            PrazoPagamentoDias = e.PrazoPagamentoDias,
            LimiteNuitObrigatorio = e.LimiteNuitObrigatorio,
            ModoArredondamentoIva = e.ModoArredondamentoIva,
        };
        await CarregarLogotipoAsync();
    }

    public async Task<IActionResult> OnPostDadosAsync()
    {
        if (ModelState.IsValid)
        {
            var d = Dados;
            var r = await empresas.ActualizarDadosAsync(new DadosEmpresa(
                d.Nome ?? "", d.Nuit ?? "", d.Slogan, d.Endereco ?? "", d.Cidade, d.Provincia, d.Telefones, d.Email, d.Site,
                d.CorPrimaria ?? "", d.RodapeTexto ?? "", d.PrazoPagamentoDias, d.LimiteNuitObrigatorio, d.ModoArredondamentoIva));
            if (r.Sucesso) return RedirecionarComSucesso("Dados da empresa guardados.");
            AdicionarErros(r);
        }
        await CarregarLogotipoAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostLogotipoAsync()
    {
        if (Logotipo is null || Logotipo.Length == 0)
        {
            Erro("Escolha um ficheiro PNG ou JPG antes de carregar.");
            return RedirectToPage();
        }
        if (Logotipo.Length > ServicoEmpresa.TamanhoMaximoLogotipo)
        {
            Erro("O logótipo tem mais de 2 MB. Reduza a imagem e tente outra vez.");
            return RedirectToPage();
        }

        using var memoria = new MemoryStream();
        await Logotipo.CopyToAsync(memoria);
        var r = await empresas.ActualizarLogotipoAsync(memoria.ToArray());
        if (r.Sucesso) return RedirecionarComSucesso("Logótipo actualizado.");
        Erro(r.Erros[0]);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoverLogotipoAsync()
    {
        await empresas.RemoverLogotipoAsync();
        return RedirecionarComSucesso("Logótipo removido.");
    }

    private async Task CarregarLogotipoAsync()
    {
        var e = await empresas.ObterAsync();
        TemLogotipo = e.LogoPath is not null;
        VersaoLogotipo = e.UpdatedAt?.Ticks ?? 0;
        NuitPorPreencher = e.Nuit == InicializadorBaseDados.NuitPorPreencher;
    }
}
