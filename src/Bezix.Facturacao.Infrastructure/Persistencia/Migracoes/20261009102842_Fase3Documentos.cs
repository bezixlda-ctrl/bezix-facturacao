using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bezix.Facturacao.Infrastructure.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Fase3Documentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TipoDoc = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    SerieId = table.Column<int>(type: "int", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: true),
                    NumeroFormatado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Vencimento = table.Column<DateOnly>(type: "date", nullable: true),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    ClienteNome = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, collation: "Latin1_General_100_CI_AI"),
                    ClienteNuit = table.Column<string>(type: "varchar(9)", unicode: false, maxLength: 9, nullable: true),
                    ClienteEndereco = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ClienteCidade = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClienteTelefone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClienteCelular = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClienteContacto = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ClienteEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmitenteJson = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: true),
                    Moeda = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    Cambio = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DescontoGlobalPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    DescontoGlobal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseTributavel = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalIva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseTributavelMzn = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalIvaMzn = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalMzn = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Observacoes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MetodoPagamento = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ReferenciaPagamento = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LocalCarga = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LocalDescarga = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Matricula = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DocOrigemId = table.Column<int>(type: "int", nullable: true),
                    MotivoCorreccao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EmitidoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmitidoPor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    HashAnterior = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    PdfPath = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AnuladoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnuladoPor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    MotivoAnulacao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResponsavelId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documentos", x => x.Id);
                    table.CheckConstraint("CK_Documentos_Cambio", "[Cambio] > 0");
                    table.CheckConstraint("CK_Documentos_NumeroEmitido", "[Estado] IN ('Rascunho','AguardaAprovacao') OR ([Numero] IS NOT NULL AND [Hash] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Documentos_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documentos_Documentos_DocOrigemId",
                        column: x => x.DocOrigemId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documentos_Series_SerieId",
                        column: x => x.SerieId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailFila",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Para = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Cc = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Bcc = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Assunto = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Corpo = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: false),
                    Anexos = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tentativas = table.Column<int>(type: "int", nullable: false),
                    ErroUltimo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EnviadoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProximaTentativa = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClienteId = table.Column<int>(type: "int", nullable: true),
                    DocumentoId = table.Column<int>(type: "int", nullable: true),
                    ModeloCodigo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailFila", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelosComunicacao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Canal = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Assunto = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Corpo = table.Column<string>(type: "nvarchar(max)", maxLength: 256, nullable: false),
                    Cc = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AnexarPdf = table.Column<bool>(type: "bit", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelosComunicacao", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Moedas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Codigo = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    Nome = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Simbolo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NomeSingular = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    NomePlural = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CentimoSingular = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CentimoPlural = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Moedas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxasCambio",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Moeda = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxasCambio", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Aprovacoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PedidoPorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PedidoPorNome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Justificacao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecididoPorNome = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DecididoEm = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NotaDecisao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aprovacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Aprovacoes_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentoIvaResumos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    TaxaPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    MotivoIsencao = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    Base = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseMzn = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IvaMzn = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentoIvaResumos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentoIvaResumos_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentoLinhas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    Ordem = table.Column<int>(type: "int", nullable: false),
                    ArtigoId = table.Column<int>(type: "int", nullable: true),
                    Ref = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Descricao = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Unidade = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Quantidade = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DescontoPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CodigoTaxaIva = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TaxaIvaPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    MotivoIsencao = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Base = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalLinha = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentoLinhas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentoLinhas_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentosOrigem",
                columns: table => new
                {
                    DocumentoId = table.Column<int>(type: "int", nullable: false),
                    OrigemId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentosOrigem", x => new { x.DocumentoId, x.OrigemId });
                    table.ForeignKey(
                        name: "FK_DocumentosOrigem_Documentos_DocumentoId",
                        column: x => x.DocumentoId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentosOrigem_Documentos_OrigemId",
                        column: x => x.OrigemId,
                        principalTable: "Documentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Aprovacoes_DocumentoId",
                table: "Aprovacoes",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Aprovacoes_Estado_DocumentoId",
                table: "Aprovacoes",
                columns: new[] { "Estado", "DocumentoId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentoIvaResumos_DocumentoId",
                table: "DocumentoIvaResumos",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentoLinhas_DocumentoId_Ordem",
                table: "DocumentoLinhas",
                columns: new[] { "DocumentoId", "Ordem" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_ClienteId_Estado",
                table: "Documentos",
                columns: new[] { "ClienteId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_Data_Estado",
                table: "Documentos",
                columns: new[] { "Data", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_DocOrigemId",
                table: "Documentos",
                column: "DocOrigemId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_NumeroFormatado",
                table: "Documentos",
                column: "NumeroFormatado");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_SerieId_Numero",
                table: "Documentos",
                columns: new[] { "SerieId", "Numero" },
                unique: true,
                filter: "[Numero] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosOrigem_OrigemId",
                table: "DocumentosOrigem",
                column: "OrigemId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailFila_ClienteId",
                table: "EmailFila",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailFila_DocumentoId",
                table: "EmailFila",
                column: "DocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailFila_Estado_ProximaTentativa",
                table: "EmailFila",
                columns: new[] { "Estado", "ProximaTentativa" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelosComunicacao_Codigo",
                table: "ModelosComunicacao",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Moedas_Codigo",
                table: "Moedas",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxasCambio_Moeda_Data",
                table: "TaxasCambio",
                columns: new[] { "Moeda", "Data" },
                unique: true);

            // Imutabilidade ao nível da base de dados: protege os documentos emitidos mesmo contra SQL directo.
            // Campos que podem mudar depois da emissão: Estado (Emitido → Anulado), dados da anulação, PdfPath e UpdatedAt.
            migrationBuilder.Sql(@"
CREATE TRIGGER [TR_Documentos_Imutavel] ON [Documentos] AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d WHERE d.[Estado] IN ('Emitido','Anulado')
               AND NOT EXISTS (SELECT 1 FROM inserted i WHERE i.[Id] = d.[Id]))
        THROW 50001, 'Documento emitido não pode ser apagado.', 1;

    IF EXISTS (
        SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
        WHERE d.[Estado] IN ('Emitido','Anulado') AND (
            (d.[Estado] = 'Anulado' AND i.[Estado] <> 'Anulado')
            OR (d.[Estado] = 'Emitido' AND i.[Estado] NOT IN ('Emitido','Anulado'))
            OR (d.[Estado] = 'Anulado' AND EXISTS (
                    SELECT i.[MotivoAnulacao], i.[AnuladoEm], i.[AnuladoPor] EXCEPT SELECT d.[MotivoAnulacao], d.[AnuladoEm], d.[AnuladoPor]))
            OR EXISTS (
                SELECT i.[TipoDoc], i.[SerieId], i.[Numero], i.[NumeroFormatado], i.[Data], i.[Vencimento], i.[ClienteId],
                       i.[ClienteNome], i.[ClienteNuit], i.[ClienteEndereco], i.[ClienteCidade], i.[EmitenteJson],
                       i.[Moeda], i.[Cambio], i.[Subtotal], i.[DescontoGlobalPct], i.[DescontoGlobal], i.[BaseTributavel],
                       i.[TotalIva], i.[Total], i.[BaseTributavelMzn], i.[TotalIvaMzn], i.[TotalMzn],
                       i.[MetodoPagamento], i.[DocOrigemId], i.[MotivoCorreccao], i.[EmitidoEm], i.[EmitidoPor], i.[Hash], i.[HashAnterior]
                EXCEPT
                SELECT d.[TipoDoc], d.[SerieId], d.[Numero], d.[NumeroFormatado], d.[Data], d.[Vencimento], d.[ClienteId],
                       d.[ClienteNome], d.[ClienteNuit], d.[ClienteEndereco], d.[ClienteCidade], d.[EmitenteJson],
                       d.[Moeda], d.[Cambio], d.[Subtotal], d.[DescontoGlobalPct], d.[DescontoGlobal], d.[BaseTributavel],
                       d.[TotalIva], d.[Total], d.[BaseTributavelMzn], d.[TotalIvaMzn], d.[TotalMzn],
                       d.[MetodoPagamento], d.[DocOrigemId], d.[MotivoCorreccao], d.[EmitidoEm], d.[EmitidoPor], d.[Hash], d.[HashAnterior])))
        THROW 50002, 'Documento emitido é imutável: corrija com nota de crédito/débito ou anulação.', 1;
END");

            foreach (var (tabela, trigger) in new[] { ("DocumentoLinhas", "TR_DocumentoLinhas_Imutavel"), ("DocumentoIvaResumos", "TR_DocumentoIvaResumos_Imutavel") })
            {
                migrationBuilder.Sql($@"
CREATE TRIGGER [{trigger}] ON [{tabela}] AFTER INSERT, UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM (SELECT [DocumentoId] FROM inserted UNION SELECT [DocumentoId] FROM deleted) x
        JOIN [Documentos] d ON d.[Id] = x.[DocumentoId]
        WHERE d.[Estado] IN ('Emitido','Anulado'))
        THROW 50003, 'As linhas de um documento emitido não podem ser alteradas.', 1;
END");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_Documentos_Imutavel]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_DocumentoLinhas_Imutavel]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_DocumentoIvaResumos_Imutavel]");

            migrationBuilder.DropTable(
                name: "Aprovacoes");

            migrationBuilder.DropTable(
                name: "DocumentoIvaResumos");

            migrationBuilder.DropTable(
                name: "DocumentoLinhas");

            migrationBuilder.DropTable(
                name: "DocumentosOrigem");

            migrationBuilder.DropTable(
                name: "EmailFila");

            migrationBuilder.DropTable(
                name: "ModelosComunicacao");

            migrationBuilder.DropTable(
                name: "Moedas");

            migrationBuilder.DropTable(
                name: "TaxasCambio");

            migrationBuilder.DropTable(
                name: "Documentos");
        }
    }
}
