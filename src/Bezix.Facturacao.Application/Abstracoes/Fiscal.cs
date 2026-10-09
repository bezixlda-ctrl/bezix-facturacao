using Bezix.Facturacao.Domain.Documentos;

namespace Bezix.Facturacao.Application.Abstracoes;

/// <summary>Dados de um mês para a AT: documentos fiscais emitidos no período (incluindo os anulados, marcados como tal).</summary>
public sealed record DadosExportacaoAT(
    int Ano,
    int Mes,
    string EmitenteNome,
    string EmitenteNuit,
    string Software,
    string? NumeroCertificado,
    DateTime GeradoEmUtc,
    IReadOnlyList<Documento> Documentos);

public sealed record FicheiroExportacaoAT(string NomeFicheiro, byte[] Conteudo);

/// <summary>
/// Formato do ficheiro mensal da AT. Componente isolado (decisão D22): quando a AT publicar a estrutura oficial,
/// cria-se outra implementação sem mexer em documentos, séries ou cálculo.
/// </summary>
public interface IExportadorAT
{
    /// <summary>Identificação do formato, guardada no histórico (ex.: "csv-provisorio-v1").</summary>
    string Formato { get; }

    FicheiroExportacaoAT Gerar(DadosExportacaoAT dados);
}

/// <summary>Identificação do software no rodapé dos documentos e na exportação (secção "Certificacao" da configuração).</summary>
public sealed class OpcoesCertificacao
{
    public string Software { get; set; } = "Bezix Facturação";
    public string Versao { get; set; } = "1.0";

    /// <summary>N.º do certificado atribuído pela AT. Vazio enquanto a certificação não estiver concluída.</summary>
    public string? NumeroCertificado { get; set; }

    public string Identificacao => $"{Software} v{Versao}";

    public string TextoRodape => string.IsNullOrWhiteSpace(NumeroCertificado)
        ? $"Software: {Identificacao} (certificação AT pendente)"
        : $"Programa certificado n.º {NumeroCertificado}/AT · {Identificacao}";
}
