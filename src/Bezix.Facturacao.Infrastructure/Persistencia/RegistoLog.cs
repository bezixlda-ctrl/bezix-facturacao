namespace Bezix.Facturacao.Infrastructure.Persistencia;

/// <summary>
/// Tabela "Registos" escrita pelo Serilog (avisos e erros). Criada pela migração,
/// para não depender de o Serilog criar tabelas no arranque.
/// As colunas seguem o formato por omissão do Serilog.Sinks.MSSqlServer.
/// </summary>
public class RegistoLog
{
    public long Id { get; set; }
    public string? Message { get; set; }
    public string? MessageTemplate { get; set; }
    public string? Level { get; set; }
    public DateTime TimeStamp { get; set; }
    public string? Exception { get; set; }
    public string? Properties { get; set; }
}
