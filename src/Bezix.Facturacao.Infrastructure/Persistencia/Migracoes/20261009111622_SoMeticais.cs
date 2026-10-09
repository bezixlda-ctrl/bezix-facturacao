using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bezix.Facturacao.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class SoMeticais : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxasCambio");

            // Os documentos são emitidos só em meticais (legislação fiscal moçambicana).
            // Retira as moedas estrangeiras que nenhum documento usa; as usadas ficam inactivas.
            migrationBuilder.Sql(@"
DELETE FROM [Moedas] WHERE [Codigo] <> 'MZN' AND NOT EXISTS (SELECT 1 FROM [Documentos] d WHERE d.[Moeda] = [Moedas].[Codigo]);
UPDATE [Moedas] SET [Activa] = 0 WHERE [Codigo] <> 'MZN';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxasCambio",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Moeda = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Valor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxasCambio", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxasCambio_Moeda_Data",
                table: "TaxasCambio",
                columns: new[] { "Moeda", "Data" },
                unique: true);
        }
    }
}
