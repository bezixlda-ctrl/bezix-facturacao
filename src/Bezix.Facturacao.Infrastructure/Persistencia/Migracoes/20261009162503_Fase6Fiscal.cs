using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bezix.Facturacao.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Fase6Fiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExportacoesAT",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ano = table.Column<int>(type: "int", nullable: false),
                    Mes = table.Column<int>(type: "int", nullable: false),
                    Formato = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NomeFicheiro = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FicheiroPath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    HashSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Tamanho = table.Column<long>(type: "bigint", nullable: false),
                    NumDocs = table.Column<int>(type: "int", nullable: false),
                    NumAnulados = table.Column<int>(type: "int", nullable: false),
                    TotalBase = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalIva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GeradoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeradoPor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SubmetidoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmetidoPor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReferenciaSubmissao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportacoesAT", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExportacoesAT_Ano_Mes",
                table: "ExportacoesAT",
                columns: new[] { "Ano", "Mes" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExportacoesAT");
        }
    }
}
