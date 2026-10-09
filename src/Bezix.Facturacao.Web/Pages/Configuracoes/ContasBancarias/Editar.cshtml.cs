using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Configuracoes.ContasBancarias;

[Authorize(Policy = Permissoes.Configuracoes.Gerir)]
public class EditarModel(ServicoContasBancarias contas) : PaginaBase
{
    public sealed class Formulario
    {
        public string Banco { get; set; } = "";
        public string NumeroConta { get; set; } = "";
        public string? Nib { get; set; }
        public string? Iban { get; set; }
        public string? Swift { get; set; }
        public bool MostrarNoDocumento { get; set; } = true;
        public int Ordem { get; set; }
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public Formulario Dados { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is null) return Page();
        var c = await contas.ObterAsync(Id.Value);
        if (c is null) return NotFound();
        Dados = new Formulario
        {
            Banco = c.Banco, NumeroConta = c.NumeroConta, Nib = c.Nib, Iban = c.Iban, Swift = c.Swift,
            MostrarNoDocumento = c.MostrarNoDocumento, Ordem = c.Ordem,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var d = Dados;
        var r = await contas.GuardarAsync(Id, new DadosContaBancaria(d.Banco ?? "", d.NumeroConta ?? "", d.Nib, d.Iban, d.Swift, d.MostrarNoDocumento, d.Ordem));
        if (r.Sucesso) return RedirecionarComSucesso(Id is null ? "Conta bancária adicionada." : "Conta bancária guardada.", "Index");
        AdicionarErros(r);
        return Page();
    }
}
