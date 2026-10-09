using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bezix.Facturacao.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Fase5Automacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssinaturaEmail",
                table: "Empresa",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiasValidadeLink",
                table: "Empresa",
                type: "int",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<string>(
                name: "UrlPublica",
                table: "Empresa",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChavesApi",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Prefixo = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    HashChave = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Escopos = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UltimoUso = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevogadaEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChavesApi", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExecucoesTarefas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Tarefa = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Inicio = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Fim = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Sucesso = table.Column<bool>(type: "bit", nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Erro = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecucoesTarefas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LembretesEnviados",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EnviadoEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmailId = table.Column<int>(type: "int", nullable: true),
                    EnviadoPor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LembretesEnviados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LinksPartilha",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrimeiraVisita = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UltimaVisita = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Visitas = table.Column<int>(type: "int", nullable: false),
                    RevogadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinksPartilha", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinksPartilha_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarcasUnicas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Chave = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarcasUnicas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PagamentosComunicados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    DocumentoId = table.Column<int>(type: "int", nullable: true),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Referencia = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Mensagem = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ComprovativoPath = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ComprovativoTipo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecididoPor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DecididoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoRejeicao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReciboId = table.Column<int>(type: "int", nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagamentosComunicados", x => x.Id);
                    table.CheckConstraint("CK_PagamentosComunicados_Valor", "[Valor] > 0");
                    table.ForeignKey(
                        name: "FK_PagamentosComunicados_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PedidosIdempotentes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChaveApiId = table.Column<int>(type: "int", nullable: false),
                    Chave = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Rota = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HashCorpo = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CodigoResposta = table.Column<int>(type: "int", nullable: true),
                    Resposta = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidosIdempotentes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanoLembretes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    DiasAntes = table.Column<int>(type: "int", nullable: false),
                    DiasDepois = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MaxAvisos = table.Column<int>(type: "int", nullable: false),
                    HoraEnvio = table.Column<int>(type: "int", nullable: false),
                    SoDiasUteis = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanoLembretes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recorrencias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Descricao = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ModeloDocumentoId = table.Column<int>(type: "int", nullable: false),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    Periodicidade = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProximaData = table.Column<DateOnly>(type: "date", nullable: false),
                    DataFim = table.Column<DateOnly>(type: "date", nullable: true),
                    EmitirAutomaticamente = table.Column<bool>(type: "bit", nullable: false),
                    EnviarEmail = table.Column<bool>(type: "bit", nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    UltimaExecucao = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UltimoDocumentoId = table.Column<int>(type: "int", nullable: true),
                    UltimoErro = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recorrencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recorrencias_Documentos_ModeloDocumentoId",
                        column: x => x.ModeloDocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscricoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SegredoCifrado = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Eventos = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    FalhasSeguidas = table.Column<int>(type: "int", nullable: false),
                    DesactivadaEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoDesactivacao = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscricoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookEntregas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscricaoId = table.Column<int>(type: "int", nullable: false),
                    EventoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Evento = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    ProximaTentativa = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CodigoResposta = table.Column<int>(type: "int", nullable: true),
                    Resposta = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EntregueEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEntregas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookEntregas_WebhookSubscricoes_SubscricaoId",
                        column: x => x.SubscricaoId,
                        principalTable: "WebhookSubscricoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChavesApi_Prefixo",
                table: "ChavesApi",
                column: "Prefixo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecucoesTarefas_Inicio",
                table: "ExecucoesTarefas",
                column: "Inicio");

            migrationBuilder.CreateIndex(
                name: "IX_LembretesEnviados_DocumentoId_Tipo",
                table: "LembretesEnviados",
                columns: new[] { "DocumentoId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_LinksPartilha_DocumentoId",
                table: "LinksPartilha",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_LinksPartilha_TokenHash",
                table: "LinksPartilha",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarcasUnicas_Chave",
                table: "MarcasUnicas",
                column: "Chave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosComunicados_ClienteId",
                table: "PagamentosComunicados",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosComunicados_Estado_CreatedAt",
                table: "PagamentosComunicados",
                columns: new[] { "Estado", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PedidosIdempotentes_ChaveApiId_Chave",
                table: "PedidosIdempotentes",
                columns: new[] { "ChaveApiId", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PedidosIdempotentes_CreatedAt",
                table: "PedidosIdempotentes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Recorrencias_Activa_ProximaData",
                table: "Recorrencias",
                columns: new[] { "Activa", "ProximaData" });

            migrationBuilder.CreateIndex(
                name: "IX_Recorrencias_ModeloDocumentoId",
                table: "Recorrencias",
                column: "ModeloDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEntregas_Estado_ProximaTentativa",
                table: "WebhookEntregas",
                columns: new[] { "Estado", "ProximaTentativa" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEntregas_SubscricaoId_CreatedAt",
                table: "WebhookEntregas",
                columns: new[] { "SubscricaoId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChavesApi");

            migrationBuilder.DropTable(
                name: "ExecucoesTarefas");

            migrationBuilder.DropTable(
                name: "LembretesEnviados");

            migrationBuilder.DropTable(
                name: "LinksPartilha");

            migrationBuilder.DropTable(
                name: "MarcasUnicas");

            migrationBuilder.DropTable(
                name: "PagamentosComunicados");

            migrationBuilder.DropTable(
                name: "PedidosIdempotentes");

            migrationBuilder.DropTable(
                name: "PlanoLembretes");

            migrationBuilder.DropTable(
                name: "Recorrencias");

            migrationBuilder.DropTable(
                name: "WebhookEntregas");

            migrationBuilder.DropTable(
                name: "WebhookSubscricoes");

            migrationBuilder.DropColumn(
                name: "AssinaturaEmail",
                table: "Empresa");

            migrationBuilder.DropColumn(
                name: "DiasValidadeLink",
                table: "Empresa");

            migrationBuilder.DropColumn(
                name: "UrlPublica",
                table: "Empresa");
        }
    }
}
