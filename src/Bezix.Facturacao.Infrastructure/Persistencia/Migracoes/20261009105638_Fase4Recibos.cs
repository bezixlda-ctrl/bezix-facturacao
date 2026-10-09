using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bezix.Facturacao.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Fase4Recibos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pagamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Metodo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Observacoes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReciboId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pagamentos", x => x.Id);
                    table.CheckConstraint("CK_Pagamentos_Valor", "[Valor] > 0");
                    table.ForeignKey(
                        name: "FK_Pagamentos_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pagamentos_Documentos_ReciboId",
                        column: x => x.ReciboId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PagamentoAlocacoes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PagamentoId = table.Column<int>(type: "int", nullable: false),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagamentoAlocacoes", x => x.Id);
                    table.CheckConstraint("CK_PagamentoAlocacoes_Valor", "[Valor] > 0");
                    table.ForeignKey(
                        name: "FK_PagamentoAlocacoes_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagamentoAlocacoes_Pagamentos_PagamentoId",
                        column: x => x.PagamentoId,
                        principalTable: "Pagamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PagamentoAlocacoes_DocumentoId",
                table: "PagamentoAlocacoes",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_PagamentoAlocacoes_PagamentoId",
                table: "PagamentoAlocacoes",
                column: "PagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_ClienteId_Data",
                table: "Pagamentos",
                columns: new[] { "ClienteId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_Pagamentos_ReciboId",
                table: "Pagamentos",
                column: "ReciboId",
                unique: true);

            // Pagamentos e alocações são registos financeiros: inserem-se, nunca se alteram nem apagam.
            // Um recibo errado anula-se (o documento RC passa a Anulado e as alocações deixam de contar).
            foreach (var (tabela, trigger) in new[] { ("Pagamentos", "TR_Pagamentos_Imutavel"), ("PagamentoAlocacoes", "TR_PagamentoAlocacoes_Imutavel") })
            {
                migrationBuilder.Sql($@"
CREATE TRIGGER [{trigger}] ON [{tabela}] AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 50004, 'Pagamentos e alocações não podem ser alterados nem apagados. Anule o recibo.', 1;
END");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_Pagamentos_Imutavel]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_PagamentoAlocacoes_Imutavel]");

            migrationBuilder.DropTable(
                name: "PagamentoAlocacoes");

            migrationBuilder.DropTable(
                name: "Pagamentos");
        }
    }
}
