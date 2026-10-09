using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Infrastructure.Identidade;
using Bezix.Facturacao.Web.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Clientes;

[Authorize(Policy = Permissoes.Clientes.Gerir)]
public class EditarModel(ServicoClientes clientes, ServicoUtilizadores utilizadores) : PaginaBase
{
    public sealed class Formulario
    {
        public string Nome { get; set; } = "";
        public TipoCliente Tipo { get; set; } = TipoCliente.Empresa;
        public string? Nuit { get; set; }
        public string? Endereco { get; set; }
        public string? Cidade { get; set; }
        public string? Telefone { get; set; }
        public string? Celular { get; set; }
        public string? Email { get; set; }
        public string? Contacto { get; set; }
        public int? PrazoPagamentoDias { get; set; }
        public decimal? LimiteCredito { get; set; }
        public decimal DescontoPadraoPct { get; set; }
        public string? Notas { get; set; }
        public string? Etiquetas { get; set; }
        public string? EmailFacturacao { get; set; }
        public string? EmailCc { get; set; }
        public bool LembretesActivos { get; set; } = true;
        public string? ResponsavelId { get; set; }
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty] public Formulario Dados { get; set; } = new();
    [BindProperty] public bool AceitarNuitDuplicado { get; set; }

    public IReadOnlyList<ClienteDuplicado> Duplicados { get; private set; } = [];
    public List<UtilizadorResumo> Responsaveis { get; private set; } = [];
    public bool PodeEscolherResponsavel => User.TemPermissao(Permissoes.Documentos.VerTodos);
    public bool Novo => Id is null;

    public async Task<IActionResult> OnGetAsync()
    {
        await CarregarResponsaveisAsync();
        if (Novo) return Page();

        var c = await clientes.ObterAsync(Id!.Value);
        if (c is null) return NotFound();
        Dados = new Formulario
        {
            Nome = c.Nome, Tipo = c.Tipo, Nuit = c.Nuit, Endereco = c.Endereco, Cidade = c.Cidade, Telefone = c.Telefone,
            Celular = c.Celular, Email = c.Email, Contacto = c.Contacto, PrazoPagamentoDias = c.PrazoPagamentoDias,
            LimiteCredito = c.LimiteCredito, DescontoPadraoPct = c.DescontoPadraoPct, Notas = c.Notas,
            Etiquetas = c.Etiquetas?.Replace(",", ", "), EmailFacturacao = c.EmailFacturacao, EmailCc = c.EmailCc,
            LembretesActivos = c.LembretesActivos, ResponsavelId = c.ResponsavelId,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var d = Dados;
            var r = await clientes.GuardarAsync(Id, new DadosCliente(
                d.Nome ?? "", d.Tipo, d.Nuit, d.Endereco, d.Cidade, d.Telefone, d.Celular, d.Email, d.Contacto,
                d.PrazoPagamentoDias, d.LimiteCredito, d.DescontoPadraoPct, d.Notas, d.Etiquetas, d.EmailFacturacao, d.EmailCc,
                d.LembretesActivos, PodeEscolherResponsavel ? d.ResponsavelId : null), AceitarNuitDuplicado);

            if (r.Resultado.Sucesso)
                return RedirecionarComSucesso(Novo ? "Cliente criado." : "Cliente guardado.", "Detalhe", new { id = r.Id });

            if (r.PrecisaConfirmacao) Duplicados = r.Duplicados;
            else AdicionarErros(r.Resultado);
        }
        await CarregarResponsaveisAsync();
        return Page();
    }

    private async Task CarregarResponsaveisAsync()
    {
        if (PodeEscolherResponsavel)
            Responsaveis = (await utilizadores.ListarAsync()).Where(u => u.Activo).ToList();
    }
}
