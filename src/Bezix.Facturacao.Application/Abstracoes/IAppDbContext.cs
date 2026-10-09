using Bezix.Facturacao.Domain.Artigos;
using Bezix.Facturacao.Domain.Automacao;
using Bezix.Facturacao.Domain.Integracoes;
using Bezix.Facturacao.Domain.Auditoria;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Comunicacao;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Fiscal;
using Bezix.Facturacao.Domain.Recebimentos;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Abstracoes;

public interface IAppDbContext
{
    DbSet<Empresa> Empresas { get; }
    DbSet<ContaBancaria> ContasBancarias { get; }
    DbSet<TaxaIva> TaxasIva { get; }
    DbSet<Serie> Series { get; }
    DbSet<RegistoAuditoria> Auditoria { get; }
    DbSet<Cliente> Clientes { get; }
    DbSet<Actividade> Actividades { get; }
    DbSet<Artigo> Artigos { get; }
    DbSet<Moeda> Moedas { get; }
    DbSet<Documento> Documentos { get; }
    DbSet<DocumentoLinha> DocumentoLinhas { get; }
    DbSet<DocumentoIvaResumo> DocumentoIvaResumos { get; }
    DbSet<DocumentoOrigem> DocumentosOrigem { get; }
    DbSet<Aprovacao> Aprovacoes { get; }
    DbSet<ModeloComunicacao> ModelosComunicacao { get; }
    DbSet<EmailFila> EmailsFila { get; }
    DbSet<Pagamento> Pagamentos { get; }
    DbSet<PagamentoAlocacao> PagamentoAlocacoes { get; }
    DbSet<LinkPartilha> LinksPartilha { get; }
    DbSet<PagamentoComunicado> PagamentosComunicados { get; }
    DbSet<PlanoLembretes> PlanosLembretes { get; }
    DbSet<LembreteEnviado> LembretesEnviados { get; }
    DbSet<Recorrencia> Recorrencias { get; }
    DbSet<ExecucaoTarefa> ExecucoesTarefas { get; }
    DbSet<MarcaUnica> MarcasUnicas { get; }
    DbSet<ChaveApi> ChavesApi { get; }
    DbSet<PedidoIdempotente> PedidosIdempotentes { get; }
    DbSet<WebhookSubscricao> WebhookSubscricoes { get; }
    DbSet<WebhookEntrega> WebhookEntregas { get; }
    DbSet<ExportacaoAT> ExportacoesAT { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Esquece alterações pendentes (ex.: depois de uma tarefa automática falhar a meio).</summary>
    void DescartarAlteracoes();
}
