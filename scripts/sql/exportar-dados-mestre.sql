/*
  Exporta clientes e artigos activos da base de dados de TESTE no formato da importação CSV
  (Clientes → Importar e Artigos → Importar), para carregar na base de dados nova de produção.
  Ver docs/arranque-producao.md, passo 4.

  Como usar (SQL Server Management Studio, ligado à BD de TESTE):
    1. Query → Results To → Results to Text  (Ctrl+T)
    2. Tools → Options → Query Results → SQL Server → Results to Text:
         Output format: Custom delimited (deixar o delimitador vazio), "Include column headers" DESLIGADO,
         Maximum number of characters displayed in each column: 8192
    3. Executar o BLOCO 1, copiar o resultado para o Bloco de Notas e gravar como clientes.csv (codificação UTF-8).
    4. Executar o BLOCO 2 e gravar como artigos.csv.

  Só lê dados: não altera nada. Cada linha já sai com aspas e ";" como o Excel em português.
  Não exporta: responsável do cliente (passa a ser quem importa), email em CC, lembretes desactivados,
  clientes e artigos inactivos. Rever estes casos à mão depois da importação.
*/
SET NOCOUNT ON;

-- ===== BLOCO 1: clientes =====
WITH linhas AS (
    SELECT 0 AS ordem, CAST(N'nome;tipo;nuit;endereco;cidade;telefone;celular;email;emailfacturacao;contacto;prazo;limitecredito;desconto;notas;etiquetas' AS nvarchar(max)) AS linha
    UNION ALL
    SELECT 1,
        CONCAT_WS(N';',
            N'"' + REPLACE(c.Nome, N'"', N'""') + N'"',
            N'"' + c.Tipo + N'"',
            N'"' + ISNULL(c.Nuit, N'') + N'"',
            N'"' + REPLACE(ISNULL(c.Endereco, N''), N'"', N'""') + N'"',
            N'"' + REPLACE(ISNULL(c.Cidade, N''), N'"', N'""') + N'"',
            N'"' + ISNULL(c.Telefone, N'') + N'"',
            N'"' + ISNULL(c.Celular, N'') + N'"',
            N'"' + ISNULL(c.Email, N'') + N'"',
            N'"' + ISNULL(c.EmailFacturacao, N'') + N'"',
            N'"' + REPLACE(ISNULL(c.Contacto, N''), N'"', N'""') + N'"',
            N'"' + ISNULL(CONVERT(nvarchar(10), c.PrazoPagamentoDias), N'') + N'"',
            N'"' + ISNULL(CONVERT(nvarchar(30), c.LimiteCredito), N'') + N'"',
            N'"' + CONVERT(nvarchar(30), c.DescontoPadraoPct) + N'"',
            N'"' + REPLACE(REPLACE(REPLACE(ISNULL(c.Notas, N''), N'"', N'""'), CHAR(13), N' '), CHAR(10), N' ') + N'"',
            N'"' + ISNULL(c.Etiquetas, N'') + N'"')
    FROM dbo.Clientes c
    WHERE c.Activo = 1
)
SELECT linha FROM linhas ORDER BY ordem, linha;

-- ===== BLOCO 2: artigos =====
WITH linhas AS (
    SELECT 0 AS ordem, CAST(N'codigo;descricao;tipo;unidade;preco;iva' AS nvarchar(max)) AS linha
    UNION ALL
    SELECT 1,
        CONCAT_WS(N';',
            N'"' + REPLACE(a.Codigo, N'"', N'""') + N'"',
            N'"' + REPLACE(a.Descricao, N'"', N'""') + N'"',
            N'"' + a.Tipo + N'"',
            N'"' + ISNULL(a.Unidade, N'un') + N'"',
            N'"' + CONVERT(nvarchar(30), a.Preco) + N'"',
            N'"' + ISNULL(a.CodigoTaxaIva, N'') + N'"')
    FROM dbo.Artigos a
    WHERE a.Activo = 1
)
SELECT linha FROM linhas ORDER BY ordem, linha;

-- ===== CONTROLO: totais para conferir depois da importação na produção =====
SELECT
    (SELECT COUNT(*) FROM dbo.Clientes WHERE Activo = 1) AS ClientesActivos,
    (SELECT COUNT(*) FROM dbo.Artigos WHERE Activo = 1) AS ArtigosActivos,
    (SELECT COUNT(*) FROM dbo.Documentos) AS DocumentosDeTeste_NaoMigrar;
