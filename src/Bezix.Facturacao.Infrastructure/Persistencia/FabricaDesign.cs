using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bezix.Facturacao.Infrastructure.Persistencia;

/// <summary>Usada só pelo "dotnet ef" para criar migrações, sem arrancar a aplicação.</summary>
public sealed class FabricaDesign : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var ligacao = Environment.GetEnvironmentVariable("BEZIX_MIGRACOES_BD")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=BezixFacturacao;Trusted_Connection=True;TrustServerCertificate=True";
        var opcoes = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ligacao, sql => sql.UseCompatibilityLevel(160))
            .Options;
        return new AppDbContext(opcoes);
    }
}
