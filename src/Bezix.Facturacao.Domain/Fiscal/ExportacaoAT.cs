namespace Bezix.Facturacao.Domain.Fiscal;

/// <summary>
/// Ficheiro mensal gerado para a AT. Cada geração fica no histórico (pode repetir-se o mesmo mês, por exemplo depois de
/// uma anulação tardia); a última de cada mês é a que conta. O ficheiro não se altera: o hash SHA-256 permite provar
/// que o que foi submetido é o que está guardado.
/// </summary>
public class ExportacaoAT
{
    public int Id { get; set; }
    public int Ano { get; set; }
    public int Mes { get; set; }

    /// <summary>Identificação do formato (ex.: "csv-provisorio-v1"); muda quando a AT publicar a estrutura oficial.</summary>
    public string Formato { get; set; } = "";
    public string NomeFicheiro { get; set; } = "";
    public string FicheiroPath { get; set; } = "";
    public string HashSha256 { get; set; } = "";
    public long Tamanho { get; set; }

    public int NumDocs { get; set; }
    public int NumAnulados { get; set; }
    public decimal TotalBase { get; set; }
    public decimal TotalIva { get; set; }

    public DateTime GeradoEm { get; set; }
    public string GeradoPor { get; set; } = "";
    public DateTime? SubmetidoEm { get; set; }
    public string? SubmetidoPor { get; set; }
    public string? ReferenciaSubmissao { get; set; }

    public string Periodo => $"{Mes:00}/{Ano}";
}
