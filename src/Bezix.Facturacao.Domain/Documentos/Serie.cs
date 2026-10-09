using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Documentos;

/// <summary>
/// Série de numeração: contador independente por tipo de documento + código + ano.
/// O número só é atribuído na transacção de emissão (com bloqueio), nunca no rascunho.
/// </summary>
public class Serie : Entidade
{
    public TipoDocumento TipoDoc { get; set; }
    public string Codigo { get; set; } = "";
    public int Ano { get; set; }
    public int ProximoNumero { get; set; } = 1;
    public EstadoSerie Estado { get; set; } = EstadoSerie.Activa;
    public DateOnly DataAbertura { get; set; }
    public DateOnly? DataFecho { get; set; }

    /// <summary>Data do último documento emitido; nenhuma emissão pode ter data anterior.</summary>
    public DateOnly? UltimaData { get; set; }

    /// <summary>Formato: "FT A2026/0001".</summary>
    public static string FormatarNumero(TipoDocumento tipo, string codigo, int ano, int numero) =>
        $"{tipo} {codigo}{ano}/{numero:0000}";

    public string Descricao => $"{TipoDoc} {Codigo}{Ano}";
}

public enum EstadoSerie
{
    Activa = 0,
    Fechada = 1,
}
