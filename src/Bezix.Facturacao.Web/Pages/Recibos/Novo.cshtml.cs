using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Recibos;

[Authorize(Policy = Permissoes.Recibos.Registar)]
public class NovoModel(ServicoRecibos recibos, ServicoContaCorrente contaCorrente, ServicoClientes clientes) : PaginaBase
{
    public sealed class AlocacaoForm
    {
        public int DocumentoId { get; set; }
        public decimal Valor { get; set; }
    }

    [BindProperty(SupportsGet = true)] public int ClienteId { get; set; }
    [BindProperty] public DateOnly Data { get; set; }
    [BindProperty] public decimal Valor { get; set; }
    [BindProperty] public MetodoPagamento Metodo { get; set; } = MetodoPagamento.Transferencia;
    [BindProperty] public string? Referencia { get; set; }
    [BindProperty] public string? Observacoes { get; set; }
    [BindProperty] public List<AlocacaoForm> Alocacoes { get; set; } = [];

    public string? NomeCliente { get; private set; }
    public List<DocumentoAberto> Abertos { get; private set; } = [];
    public decimal Adiantamento { get; private set; }

    public async Task OnGetAsync(int? documentoId)
    {
        Data = contaCorrente.Hoje();
        await CarregarAsync();
        // Vindo de uma factura: propõe pagar o que falta dessa factura.
        if (documentoId is { } id && Abertos.FirstOrDefault(a => a.Id == id) is { } doc)
        {
            Valor = doc.EmAberto;
            Alocacoes = [new AlocacaoForm { DocumentoId = id, Valor = doc.EmAberto }];
        }
    }

    public async Task<IActionResult> OnGetAbertosAsync()
    {
        await CarregarAsync();
        return Partial("_Abertos", this);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var (r, id) = await recibos.RegistarAsync(new DadosRecibo(ClienteId, Data, Valor, Metodo, Referencia, Observacoes,
                Alocacoes.Where(a => a.Valor > 0).Select(a => new Alocacao(a.DocumentoId, a.Valor)).ToList()));
            if (r.Sucesso) return RedirecionarComSucesso("Recibo emitido.", "/Documentos/Detalhe", new { id });
            AdicionarErros(r);
        }
        await CarregarAsync();
        return Page();
    }

    private async Task CarregarAsync()
    {
        if (ClienteId <= 0) return;
        var c = await clientes.ObterAsync(ClienteId);
        if (c is null) { ClienteId = 0; return; }
        NomeCliente = c.Nome;
        Abertos = await contaCorrente.EmAbertoAsync(ClienteId);
        Adiantamento = await contaCorrente.AdiantamentoAsync(ClienteId);
    }

    /// <summary>Valor alocado a um documento no formulário (para reapresentar depois de um erro).</summary>
    public decimal? ValorAlocado(int documentoId) => Alocacoes.FirstOrDefault(a => a.DocumentoId == documentoId)?.Valor;
}
