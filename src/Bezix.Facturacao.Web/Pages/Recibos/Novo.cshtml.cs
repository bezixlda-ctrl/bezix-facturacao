using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Recibos;

[Authorize(Policy = Permissoes.Recibos.Registar)]
public class NovoModel(ServicoRecibos recibos, ServicoContaCorrente contaCorrente, ServicoClientes clientes,
    ServicoPagamentosComunicados comunicados) : PaginaBase
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
    /// <summary>Pagamento comunicado pelo cliente que este recibo valida.</summary>
    [BindProperty(SupportsGet = true)] public int? ComunicadoId { get; set; }

    public PagamentoComunicadoVista? Comunicado { get; private set; }

    public string? NomeCliente { get; private set; }
    public List<DocumentoAberto> Abertos { get; private set; } = [];
    public decimal Adiantamento { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? documentoId)
    {
        Data = contaCorrente.Hoje();
        if (ComunicadoId is { } cid)
        {
            Comunicado = await comunicados.ObterAsync(cid);
            if (Comunicado is null || Comunicado.Estado != Domain.Comunicacao.EstadoPagamentoComunicado.PorValidar)
            {
                Erro("Este pagamento comunicado já foi tratado.");
                return RedirectToPage("Comunicados");
            }
            ClienteId = Comunicado.ClienteId;
            Data = Comunicado.Data;
            Valor = Comunicado.Valor;
            Referencia = Comunicado.Referencia;
            Metodo = MetodoPagamento.Transferencia;
            documentoId ??= Comunicado.DocumentoId;
        }
        await CarregarAsync();
        // Vindo de uma factura: propõe pagar o que falta dessa factura.
        if (documentoId is { } id && Abertos.FirstOrDefault(a => a.Id == id) is { } doc)
        {
            if (Comunicado is null) Valor = doc.EmAberto;
            Alocacoes = [new AlocacaoForm { DocumentoId = id, Valor = Math.Min(Valor, doc.EmAberto) }];
        }
        return Page();
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
                Alocacoes.Where(a => a.Valor > 0).Select(a => new Alocacao(a.DocumentoId, a.Valor)).ToList(), ComunicadoId));
            if (r.Sucesso) return RedirecionarComSucesso("Recibo emitido.", "/Documentos/Detalhe", new { id });
            AdicionarErros(r);
        }
        await CarregarAsync();
        return Page();
    }

    private async Task CarregarAsync()
    {
        if (ComunicadoId is { } cid && Comunicado is null) Comunicado = await comunicados.ObterAsync(cid);
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
