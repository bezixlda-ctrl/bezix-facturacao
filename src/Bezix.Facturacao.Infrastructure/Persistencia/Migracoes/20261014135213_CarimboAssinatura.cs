using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bezix.Facturacao.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class CarimboAssinatura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssinaturaPath",
                table: "Empresa",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarimboPath",
                table: "Empresa",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatarioCargo",
                table: "Empresa",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatarioNome",
                table: "Empresa",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssinaturaPath",
                table: "Empresa");

            migrationBuilder.DropColumn(
                name: "CarimboPath",
                table: "Empresa");

            migrationBuilder.DropColumn(
                name: "SignatarioCargo",
                table: "Empresa");

            migrationBuilder.DropColumn(
                name: "SignatarioNome",
                table: "Empresa");
        }
    }
}
