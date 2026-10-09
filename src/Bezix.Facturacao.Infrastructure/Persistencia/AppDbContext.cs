using Bezix.Facturacao.Application.Abstracoes;
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
using Bezix.Facturacao.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Infrastructure.Persistencia;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<Utilizador, Perfil, string>(options), IAppDbContext
{
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<ContaBancaria> ContasBancarias => Set<ContaBancaria>();
    public DbSet<TaxaIva> TaxasIva => Set<TaxaIva>();
    public DbSet<Serie> Series => Set<Serie>();
    public DbSet<RegistoAuditoria> Auditoria => Set<RegistoAuditoria>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Actividade> Actividades => Set<Actividade>();
    public DbSet<Artigo> Artigos => Set<Artigo>();
    public DbSet<Moeda> Moedas => Set<Moeda>();
    public DbSet<Documento> Documentos => Set<Documento>();
    public DbSet<DocumentoLinha> DocumentoLinhas => Set<DocumentoLinha>();
    public DbSet<DocumentoIvaResumo> DocumentoIvaResumos => Set<DocumentoIvaResumo>();
    public DbSet<DocumentoOrigem> DocumentosOrigem => Set<DocumentoOrigem>();
    public DbSet<Aprovacao> Aprovacoes => Set<Aprovacao>();
    public DbSet<ModeloComunicacao> ModelosComunicacao => Set<ModeloComunicacao>();
    public DbSet<EmailFila> EmailsFila => Set<EmailFila>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<PagamentoAlocacao> PagamentoAlocacoes => Set<PagamentoAlocacao>();
    public DbSet<LinkPartilha> LinksPartilha => Set<LinkPartilha>();
    public DbSet<PagamentoComunicado> PagamentosComunicados => Set<PagamentoComunicado>();
    public DbSet<PlanoLembretes> PlanosLembretes => Set<PlanoLembretes>();
    public DbSet<LembreteEnviado> LembretesEnviados => Set<LembreteEnviado>();
    public DbSet<Recorrencia> Recorrencias => Set<Recorrencia>();
    public DbSet<ExecucaoTarefa> ExecucoesTarefas => Set<ExecucaoTarefa>();
    public DbSet<MarcaUnica> MarcasUnicas => Set<MarcaUnica>();
    public DbSet<ChaveApi> ChavesApi => Set<ChaveApi>();
    public DbSet<PedidoIdempotente> PedidosIdempotentes => Set<PedidoIdempotente>();
    public DbSet<WebhookSubscricao> WebhookSubscricoes => Set<WebhookSubscricao>();
    public DbSet<WebhookEntrega> WebhookEntregas => Set<WebhookEntrega>();
    public DbSet<ExportacaoAT> ExportacoesAT => Set<ExportacaoAT>();

    // Triggers que impedem alterar documentos emitidos mesmo por SQL directo (ver migração Fase3Documentos).
    public const string TriggerDocumentos = "TR_Documentos_Imutavel";
    public const string TriggerLinhas = "TR_DocumentoLinhas_Imutavel";
    public const string TriggerIvaResumo = "TR_DocumentoIvaResumos_Imutavel";
    public const string TriggerPagamentos = "TR_Pagamentos_Imutavel";
    public const string TriggerAlocacoes = "TR_PagamentoAlocacoes_Imutavel";

    /// <summary>Colação sem distinção de maiúsculas nem acentos: pesquisar "conceicao" encontra "Conceição".</summary>
    public const string ColacaoPesquisa = "Latin1_General_100_CI_AI";

    public void DescartarAlteracoes() => ChangeTracker.Clear();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Valores monetários: decimal(18,2). Quantidades e percentagens configuradas explicitamente.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(256);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Utilizador>(e =>
        {
            e.ToTable("Utilizadores");
            e.Property(u => u.Nome).HasMaxLength(150);
            e.Property(u => u.AssinaturaEmail).HasMaxLength(2000);
        });
        b.Entity<Perfil>(e =>
        {
            e.ToTable("Perfis");
            e.Property(p => p.Descricao).HasMaxLength(300);
            e.Property(p => p.DescontoMaximoPct).HasPrecision(5, 2);
        });
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().ToTable("UtilizadorPerfis");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<string>>().ToTable("PerfilPermissoes");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<string>>().ToTable("UtilizadorClaims");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<string>>().ToTable("UtilizadorLogins");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<string>>().ToTable("UtilizadorTokens");

        b.Entity<Empresa>(e =>
        {
            e.ToTable("Empresa");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Nome).HasMaxLength(200).IsRequired();
            e.Property(x => x.Nuit).HasMaxLength(20).IsRequired();
            e.Property(x => x.Endereco).HasMaxLength(300).IsRequired();
            e.Property(x => x.Slogan).HasMaxLength(200);
            e.Property(x => x.CorPrimaria).HasMaxLength(7).IsRequired();
            e.Property(x => x.RodapeTexto).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Moeda).HasMaxLength(3).IsRequired();
            e.Property(x => x.ModoArredondamentoIva).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AssinaturaEmail).HasMaxLength(1000);
            e.Property(x => x.UrlPublica).HasMaxLength(200);
            e.ToTable(t => t.HasCheckConstraint("CK_Empresa_Unica", $"[Id] = {Empresa.IdUnico}"));
        });

        b.Entity<ContaBancaria>(e =>
        {
            e.ToTable("ContasBancarias");
            e.Property(x => x.Banco).HasMaxLength(100).IsRequired();
            e.Property(x => x.NumeroConta).HasMaxLength(50).IsRequired();
            e.Property(x => x.Nib).HasMaxLength(50);
            e.Property(x => x.Iban).HasMaxLength(50);
            e.Property(x => x.Swift).HasMaxLength(20);
        });

        b.Entity<TaxaIva>(e =>
        {
            e.ToTable("TaxasIva");
            e.Property(x => x.Codigo).HasMaxLength(20).IsRequired();
            e.Property(x => x.Descricao).HasMaxLength(150).IsRequired();
            e.Property(x => x.Percentagem).HasPrecision(5, 2);
            e.Property(x => x.MotivoIsencao).HasMaxLength(300);
            e.HasIndex(x => new { x.Codigo, x.VigenteDesde }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_TaxaIva_Isencao", "[Percentagem] > 0 OR [MotivoIsencao] IS NOT NULL"));
        });

        b.Entity<Serie>(e =>
        {
            e.ToTable("Series");
            e.Property(x => x.TipoDoc).HasConversion<string>().HasMaxLength(2);
            e.Property(x => x.Codigo).HasMaxLength(10).IsRequired();
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(x => new { x.TipoDoc, x.Codigo, x.Ano }).IsUnique();
            e.Ignore(x => x.Descricao);
        });

        b.Entity<Cliente>(e =>
        {
            e.ToTable("Clientes");
            e.Property(x => x.Nome).HasMaxLength(200).IsRequired().UseCollation(ColacaoPesquisa);
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Nuit).HasMaxLength(9).IsUnicode(false);
            e.Property(x => x.Endereco).HasMaxLength(300);
            e.Property(x => x.Cidade).HasMaxLength(100);
            e.Property(x => x.Telefone).HasMaxLength(50);
            e.Property(x => x.Celular).HasMaxLength(50);
            e.Property(x => x.TelefonesPesquisa).HasMaxLength(60).IsUnicode(false);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Contacto).HasMaxLength(150).UseCollation(ColacaoPesquisa);
            e.Property(x => x.DescontoPadraoPct).HasPrecision(5, 2);
            e.Property(x => x.Notas).HasMaxLength(2000);
            e.Property(x => x.Etiquetas).HasMaxLength(500);
            e.Property(x => x.EmailFacturacao).HasMaxLength(200);
            e.Property(x => x.EmailCc).HasMaxLength(500);
            e.Property(x => x.ResponsavelId).HasMaxLength(450);
            e.Ignore(x => x.EmailEnvio);
            // NUIT não é único: filiais da mesma empresa podem partilhá-lo (o sistema avisa e pede confirmação).
            e.HasIndex(x => x.Nuit);
            e.HasIndex(x => new { x.Activo, x.Nome });
            e.HasIndex(x => x.ResponsavelId);
            e.HasMany(x => x.Actividades).WithOne(a => a.Cliente).HasForeignKey(a => a.ClienteId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Actividade>(e =>
        {
            e.ToTable("Actividades");
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Nota).HasMaxLength(2000).IsRequired();
            e.Property(x => x.ResponsavelId).HasMaxLength(450);
            e.Property(x => x.ResponsavelNome).HasMaxLength(150);
            e.HasIndex(x => new { x.ClienteId, x.Data });
        });

        b.Entity<Artigo>(e =>
        {
            e.ToTable("Artigos");
            e.Property(x => x.Codigo).HasMaxLength(30).IsRequired();
            e.Property(x => x.Descricao).HasMaxLength(500).IsRequired().UseCollation(ColacaoPesquisa);
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Unidade).HasMaxLength(20).IsRequired();
            e.Property(x => x.CodigoTaxaIva).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Codigo).IsUnique();
            e.HasIndex(x => new { x.Activo, x.Codigo });
        });

        b.Entity<Moeda>(e =>
        {
            e.ToTable("Moedas");
            e.Property(x => x.Codigo).HasMaxLength(3).IsUnicode(false).IsRequired();
            e.Property(x => x.Nome).HasMaxLength(60).IsRequired();
            e.Property(x => x.Simbolo).HasMaxLength(10).IsRequired();
            e.Property(x => x.NomeSingular).HasMaxLength(40).IsRequired();
            e.Property(x => x.NomePlural).HasMaxLength(40).IsRequired();
            e.Property(x => x.CentimoSingular).HasMaxLength(40).IsRequired();
            e.Property(x => x.CentimoPlural).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Ignore(x => x.IsBase);
        });

        b.Entity<Documento>(e =>
        {
            e.ToTable("Documentos", t =>
            {
                t.HasTrigger(TriggerDocumentos);
                t.HasCheckConstraint("CK_Documentos_Cambio", "[Cambio] > 0");
                t.HasCheckConstraint("CK_Documentos_NumeroEmitido", "[Estado] IN ('Rascunho','AguardaAprovacao') OR ([Numero] IS NOT NULL AND [Hash] IS NOT NULL)");
            });
            e.Property(x => x.TipoDoc).HasConversion<string>().HasMaxLength(2);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.NumeroFormatado).HasMaxLength(30);
            e.Property(x => x.ClienteNome).HasMaxLength(200).IsRequired().UseCollation(ColacaoPesquisa);
            e.Property(x => x.ClienteNuit).HasMaxLength(9).IsUnicode(false);
            e.Property(x => x.ClienteEndereco).HasMaxLength(300);
            e.Property(x => x.ClienteCidade).HasMaxLength(100);
            e.Property(x => x.ClienteTelefone).HasMaxLength(50);
            e.Property(x => x.ClienteCelular).HasMaxLength(50);
            e.Property(x => x.ClienteContacto).HasMaxLength(150);
            e.Property(x => x.ClienteEmail).HasMaxLength(200);
            e.Property(x => x.EmitenteJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.Moeda).HasMaxLength(3).IsUnicode(false).IsRequired();
            e.Property(x => x.Cambio).HasPrecision(18, 6);
            e.Property(x => x.DescontoGlobalPct).HasPrecision(5, 2);
            e.Property(x => x.Observacoes).HasMaxLength(2000);
            e.Property(x => x.MetodoPagamento).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ReferenciaPagamento).HasMaxLength(100);
            e.Property(x => x.LocalCarga).HasMaxLength(200);
            e.Property(x => x.LocalDescarga).HasMaxLength(200);
            e.Property(x => x.Matricula).HasMaxLength(20);
            e.Property(x => x.MotivoCorreccao).HasMaxLength(500);
            e.Property(x => x.EmitidoPor).HasMaxLength(256);
            e.Property(x => x.Hash).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.HashAnterior).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.PdfPath).HasMaxLength(256);
            e.Property(x => x.AnuladoPor).HasMaxLength(256);
            e.Property(x => x.MotivoAnulacao).HasMaxLength(500);
            e.Property(x => x.ResponsavelId).HasMaxLength(450);
            e.Ignore(x => x.Emitido);
            e.Ignore(x => x.EmMoedaEstrangeira);

            e.HasOne(x => x.Serie).WithMany().HasForeignKey(x => x.SerieId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DocOrigem).WithMany().HasForeignKey(x => x.DocOrigemId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Linhas).WithOne().HasForeignKey(l => l.DocumentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.IvaResumo).WithOne().HasForeignKey(r => r.DocumentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Origens).WithOne().HasForeignKey(o => o.DocumentoId).OnDelete(DeleteBehavior.Restrict);

            // Numeração: nunca dois documentos com o mesmo número na mesma série.
            e.HasIndex(x => new { x.SerieId, x.Numero }).IsUnique().HasFilter("[Numero] IS NOT NULL");
            e.HasIndex(x => new { x.Data, x.Estado });
            e.HasIndex(x => new { x.ClienteId, x.Estado });
            e.HasIndex(x => x.DocOrigemId);
            e.HasIndex(x => x.NumeroFormatado);
        });

        b.Entity<DocumentoLinha>(e =>
        {
            e.ToTable("DocumentoLinhas", t => t.HasTrigger(TriggerLinhas));
            e.Property(x => x.Ref).HasMaxLength(30);
            e.Property(x => x.Descricao).HasMaxLength(500).IsRequired();
            e.Property(x => x.Unidade).HasMaxLength(20);
            e.Property(x => x.Quantidade).HasPrecision(18, 4);
            e.Property(x => x.DescontoPct).HasPrecision(5, 2);
            e.Property(x => x.CodigoTaxaIva).HasMaxLength(20).IsRequired();
            e.Property(x => x.TaxaIvaPct).HasPrecision(5, 2);
            e.Property(x => x.MotivoIsencao).HasMaxLength(300);
            e.HasIndex(x => new { x.DocumentoId, x.Ordem });
        });

        b.Entity<DocumentoIvaResumo>(e =>
        {
            e.ToTable("DocumentoIvaResumos", t => t.HasTrigger(TriggerIvaResumo));
            e.Property(x => x.TaxaPct).HasPrecision(5, 2);
            e.Property(x => x.MotivoIsencao).HasMaxLength(600);
            e.HasIndex(x => x.DocumentoId);
        });

        b.Entity<DocumentoOrigem>(e =>
        {
            e.ToTable("DocumentosOrigem");
            e.HasKey(x => new { x.DocumentoId, x.OrigemId });
            e.HasOne(x => x.Origem).WithMany().HasForeignKey(x => x.OrigemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.OrigemId);
        });

        b.Entity<Aprovacao>(e =>
        {
            e.ToTable("Aprovacoes");
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PedidoPorId).HasMaxLength(450).IsRequired();
            e.Property(x => x.PedidoPorNome).HasMaxLength(150).IsRequired();
            e.Property(x => x.Justificacao).HasMaxLength(500);
            e.Property(x => x.DecididoPorNome).HasMaxLength(150);
            e.Property(x => x.NotaDecisao).HasMaxLength(500);
            e.HasOne(x => x.Documento).WithMany().HasForeignKey(x => x.DocumentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Estado, x.DocumentoId });
        });

        b.Entity<ModeloComunicacao>(e =>
        {
            e.ToTable("ModelosComunicacao");
            e.Property(x => x.Codigo).HasMaxLength(50).IsRequired();
            e.Property(x => x.Nome).HasMaxLength(150).IsRequired();
            e.Property(x => x.Canal).HasMaxLength(20).IsRequired();
            e.Property(x => x.Assunto).HasMaxLength(300).IsRequired();
            e.Property(x => x.Corpo).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.Cc).HasMaxLength(500);
            e.HasIndex(x => x.Codigo).IsUnique();
        });

        b.Entity<EmailFila>(e =>
        {
            e.ToTable("EmailFila");
            e.Property(x => x.Para).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Cc).HasMaxLength(1000);
            e.Property(x => x.Bcc).HasMaxLength(1000);
            e.Property(x => x.Assunto).HasMaxLength(300).IsRequired();
            e.Property(x => x.Corpo).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.Anexos).HasMaxLength(2000);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ErroUltimo).HasMaxLength(500);
            e.Property(x => x.ModeloCodigo).HasMaxLength(50);
            e.HasIndex(x => new { x.Estado, x.ProximaTentativa });
            e.HasIndex(x => x.DocumentoId);
            e.HasIndex(x => x.ClienteId);
        });

        b.Entity<Pagamento>(e =>
        {
            e.ToTable("Pagamentos", t =>
            {
                t.HasTrigger(TriggerPagamentos);
                t.HasCheckConstraint("CK_Pagamentos_Valor", "[Valor] > 0");
            });
            e.Property(x => x.Metodo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Referencia).HasMaxLength(100);
            e.Property(x => x.Observacoes).HasMaxLength(2000);
            e.HasOne(x => x.Recibo).WithMany().HasForeignKey(x => x.ReciboId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Alocacoes).WithOne(a => a.Pagamento).HasForeignKey(a => a.PagamentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ReciboId).IsUnique();
            e.HasIndex(x => new { x.ClienteId, x.Data });
        });

        b.Entity<PagamentoAlocacao>(e =>
        {
            e.ToTable("PagamentoAlocacoes", t =>
            {
                t.HasTrigger(TriggerAlocacoes);
                t.HasCheckConstraint("CK_PagamentoAlocacoes_Valor", "[Valor] > 0");
            });
            e.HasOne(x => x.Documento).WithMany().HasForeignKey(x => x.DocumentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.DocumentoId);
        });

        b.Entity<LinkPartilha>(e =>
        {
            e.ToTable("LinksPartilha");
            e.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.DocumentoId);
            e.HasOne<Documento>().WithMany().HasForeignKey(x => x.DocumentoId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PagamentoComunicado>(e =>
        {
            e.ToTable("PagamentosComunicados", t => t.HasCheckConstraint("CK_PagamentosComunicados_Valor", "[Valor] > 0"));
            e.Property(x => x.Referencia).HasMaxLength(100);
            e.Property(x => x.Mensagem).HasMaxLength(1000);
            e.Property(x => x.ComprovativoPath).HasMaxLength(256);
            e.Property(x => x.ComprovativoTipo).HasMaxLength(50);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DecididoPor).HasMaxLength(256);
            e.Property(x => x.MotivoRejeicao).HasMaxLength(500);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Estado, x.CreatedAt });
        });

        b.Entity<PlanoLembretes>(e =>
        {
            e.ToTable("PlanoLembretes");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.DiasDepois).HasMaxLength(50);
        });

        b.Entity<LembreteEnviado>(e =>
        {
            e.ToTable("LembretesEnviados");
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EnviadoPor).HasMaxLength(256);
            e.HasIndex(x => new { x.DocumentoId, x.Tipo });
        });

        b.Entity<Recorrencia>(e =>
        {
            e.ToTable("Recorrencias");
            e.Property(x => x.Descricao).HasMaxLength(200).IsRequired();
            e.Property(x => x.Periodicidade).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.UltimoErro).HasMaxLength(1000);
            e.HasOne<Documento>().WithMany().HasForeignKey(x => x.ModeloDocumentoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Activa, x.ProximaData });
        });

        b.Entity<ExecucaoTarefa>(e =>
        {
            e.ToTable("ExecucoesTarefas");
            e.Property(x => x.Tarefa).HasMaxLength(100).IsRequired();
            e.Property(x => x.Resultado).HasMaxLength(1000);
            e.Property(x => x.Erro).HasMaxLength(2000);
            e.HasIndex(x => x.Inicio);
        });

        b.Entity<MarcaUnica>(e =>
        {
            e.ToTable("MarcasUnicas");
            e.Property(x => x.Chave).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Chave).IsUnique();
        });

        b.Entity<ChaveApi>(e =>
        {
            e.ToTable("ChavesApi");
            e.Property(x => x.Nome).HasMaxLength(100).IsRequired();
            e.Property(x => x.Prefixo).HasMaxLength(20).IsUnicode(false).IsRequired();
            e.Property(x => x.HashChave).HasMaxLength(64).IsUnicode(false).IsRequired();
            e.Property(x => x.Escopos).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.Prefixo).IsUnique();
            e.Ignore(x => x.Activa);
        });

        b.Entity<PedidoIdempotente>(e =>
        {
            e.ToTable("PedidosIdempotentes");
            e.Property(x => x.Chave).HasMaxLength(100).IsRequired();
            e.Property(x => x.Rota).HasMaxLength(200).IsRequired();
            e.Property(x => x.HashCorpo).HasMaxLength(64).IsUnicode(false).IsRequired();
            e.Property(x => x.Resposta).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.ChaveApiId, x.Chave }).IsUnique();
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<WebhookSubscricao>(e =>
        {
            e.ToTable("WebhookSubscricoes");
            e.Property(x => x.Nome).HasMaxLength(100).IsRequired();
            e.Property(x => x.Url).HasMaxLength(500).IsRequired();
            e.Property(x => x.SegredoCifrado).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Eventos).HasMaxLength(500).IsRequired();
            e.Property(x => x.MotivoDesactivacao).HasMaxLength(300);
        });

        b.Entity<WebhookEntrega>(e =>
        {
            e.ToTable("WebhookEntregas");
            e.Property(x => x.Evento).HasMaxLength(50).IsRequired();
            e.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Resposta).HasMaxLength(1000);
            e.HasOne(x => x.Subscricao).WithMany().HasForeignKey(x => x.SubscricaoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Estado, x.ProximaTentativa });
            e.HasIndex(x => new { x.SubscricaoId, x.CreatedAt });
        });

        b.Entity<ExportacaoAT>(e =>
        {
            e.ToTable("ExportacoesAT");
            e.Property(x => x.Formato).HasMaxLength(50).IsRequired();
            e.Property(x => x.NomeFicheiro).HasMaxLength(100).IsRequired();
            e.Property(x => x.FicheiroPath).HasMaxLength(300).IsRequired();
            e.Property(x => x.HashSha256).HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.TotalBase).HasPrecision(18, 2);
            e.Property(x => x.TotalIva).HasPrecision(18, 2);
            e.Property(x => x.GeradoPor).HasMaxLength(256).IsRequired();
            e.Property(x => x.SubmetidoPor).HasMaxLength(256);
            e.Property(x => x.ReferenciaSubmissao).HasMaxLength(100);
            e.Ignore(x => x.Periodo);
            e.HasIndex(x => new { x.Ano, x.Mes });
        });

        b.Entity<RegistoLog>(e =>
        {
            e.ToTable("Registos");
            e.Property(x => x.Message).HasColumnType("nvarchar(max)");
            e.Property(x => x.MessageTemplate).HasColumnType("nvarchar(max)");
            e.Property(x => x.Level).HasMaxLength(128);
            e.Property(x => x.TimeStamp).HasColumnType("datetime2");
            e.Property(x => x.Exception).HasColumnType("nvarchar(max)");
            e.Property(x => x.Properties).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.TimeStamp);
        });

        b.Entity<RegistoAuditoria>(e =>
        {
            e.ToTable("Auditoria");
            e.Property(x => x.Entidade).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntidadeId).HasMaxLength(100);
            e.Property(x => x.Accao).HasMaxLength(100).IsRequired();
            e.Property(x => x.Antes).HasColumnType("nvarchar(max)");
            e.Property(x => x.Depois).HasColumnType("nvarchar(max)");
            e.Property(x => x.Ip).HasMaxLength(64);
            e.HasIndex(x => x.Data);
            e.HasIndex(x => new { x.Entidade, x.EntidadeId });
        });
    }
}
