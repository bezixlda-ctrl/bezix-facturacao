using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bezix.Facturacao.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Auditoria",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Entidade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntidadeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Accao = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Antes = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    Depois = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    Utilizador = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Data = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Auditoria", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContasBancarias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Banco = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NumeroConta = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Nib = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Iban = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Swift = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    MostrarNoDocumento = table.Column<bool>(type: "bit", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContasBancarias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Empresa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Nuit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Slogan = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Endereco = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Cidade = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Provincia = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Telefones = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Site = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LogoPath = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CorPrimaria = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    RodapeTexto = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Moeda = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PrazoPagamentoDias = table.Column<int>(type: "int", nullable: false),
                    LimiteNuitObrigatorio = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ModoArredondamentoIva = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Empresa", x => x.Id);
                    table.CheckConstraint("CK_Empresa_Unica", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Perfis",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Sistema = table.Column<bool>(type: "bit", nullable: false),
                    DescontoMaximoPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Perfis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Registos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Message = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    MessageTemplate = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    Level = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TimeStamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Exception = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Registos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TipoDoc = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Ano = table.Column<int>(type: "int", nullable: false),
                    ProximoNumero = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DataAbertura = table.Column<DateOnly>(type: "date", nullable: false),
                    DataFecho = table.Column<DateOnly>(type: "date", nullable: true),
                    UltimaData = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxasIva",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Descricao = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Percentagem = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    MotivoIsencao = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    VigenteDesde = table.Column<DateOnly>(type: "date", nullable: false),
                    VigenteAte = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxasIva", x => x.Id);
                    table.CheckConstraint("CK_TaxaIva_Isencao", "[Percentagem] > 0 OR [MotivoIsencao] IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "Utilizadores",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    AssinaturaEmail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UltimoLogin = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Utilizadores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PerfilPermissoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerfilPermissoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerfilPermissoes_Perfis_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Perfis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UtilizadorClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilizadorClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UtilizadorClaims_Utilizadores_UserId",
                        column: x => x.UserId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UtilizadorLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilizadorLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UtilizadorLogins_Utilizadores_UserId",
                        column: x => x.UserId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UtilizadorPerfis",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilizadorPerfis", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UtilizadorPerfis_Perfis_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Perfis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UtilizadorPerfis_Utilizadores_UserId",
                        column: x => x.UserId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UtilizadorTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilizadorTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UtilizadorTokens_Utilizadores_UserId",
                        column: x => x.UserId,
                        principalTable: "Utilizadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Auditoria_Data",
                table: "Auditoria",
                column: "Data");

            migrationBuilder.CreateIndex(
                name: "IX_Auditoria_Entidade_EntidadeId",
                table: "Auditoria",
                columns: new[] { "Entidade", "EntidadeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PerfilPermissoes_RoleId",
                table: "PerfilPermissoes",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "Perfis",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Registos_TimeStamp",
                table: "Registos",
                column: "TimeStamp");

            migrationBuilder.CreateIndex(
                name: "IX_Series_TipoDoc_Codigo_Ano",
                table: "Series",
                columns: new[] { "TipoDoc", "Codigo", "Ano" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxasIva_Codigo_VigenteDesde",
                table: "TaxasIva",
                columns: new[] { "Codigo", "VigenteDesde" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UtilizadorClaims_UserId",
                table: "UtilizadorClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "Utilizadores",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "Utilizadores",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UtilizadorLogins_UserId",
                table: "UtilizadorLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UtilizadorPerfis_RoleId",
                table: "UtilizadorPerfis",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Auditoria");

            migrationBuilder.DropTable(
                name: "ContasBancarias");

            migrationBuilder.DropTable(
                name: "Empresa");

            migrationBuilder.DropTable(
                name: "PerfilPermissoes");

            migrationBuilder.DropTable(
                name: "Registos");

            migrationBuilder.DropTable(
                name: "Series");

            migrationBuilder.DropTable(
                name: "TaxasIva");

            migrationBuilder.DropTable(
                name: "UtilizadorClaims");

            migrationBuilder.DropTable(
                name: "UtilizadorLogins");

            migrationBuilder.DropTable(
                name: "UtilizadorPerfis");

            migrationBuilder.DropTable(
                name: "UtilizadorTokens");

            migrationBuilder.DropTable(
                name: "Perfis");

            migrationBuilder.DropTable(
                name: "Utilizadores");
        }
    }
}
