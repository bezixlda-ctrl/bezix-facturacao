using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Artigos;
using Bezix.Facturacao.Domain.Auditoria;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Fiscal;
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

    /// <summary>Colação sem distinção de maiúsculas nem acentos: pesquisar "conceicao" encontra "Conceição".</summary>
    public const string ColacaoPesquisa = "Latin1_General_100_CI_AI";

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
