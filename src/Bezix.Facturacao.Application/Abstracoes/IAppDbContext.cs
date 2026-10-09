using Bezix.Facturacao.Domain.Artigos;
using Bezix.Facturacao.Domain.Auditoria;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Fiscal;
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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
