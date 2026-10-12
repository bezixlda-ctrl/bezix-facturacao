using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Documentos;

namespace Bezix.Facturacao.Infrastructure.Fiscal;

/// <summary>
/// Formato provisório enquanto a AT não envia a estrutura oficial (ofício da fase 1): ZIP com três CSV (RFC 4180,
/// UTF-8, separador vírgula, ponto decimal, datas ISO 8601) e um LEIA-ME com os totais de controlo e o SHA-256 de cada CSV.
/// Tem todos os campos que um ficheiro fiscal costuma pedir, para a conversão para o formato oficial ser só de forma.
/// </summary>
public sealed class ExportadorCsvProvisorio : IExportadorAT
{
    public string Formato => "csv-provisorio-v1";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public FicheiroExportacaoAT Gerar(DadosExportacaoAT d)
    {
        var docs = d.Documentos.OrderBy(x => x.TipoDoc).ThenBy(x => x.SerieId).ThenBy(x => x.Numero).ToList();
        var ficheiros = new (string Nome, string Conteudo)[]
        {
            ("documentos.csv", Documentos(docs)),
            ("linhas.csv", Linhas(docs)),
            ("iva.csv", Iva(docs)),
        };

        using var zip = new MemoryStream();
        using (var arquivo = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nome, conteudo) in ficheiros) Escrever(arquivo, nome, conteudo, d.GeradoEmUtc);
            Escrever(arquivo, "LEIA-ME.txt", LeiaMe(d, docs, ficheiros), d.GeradoEmUtc);
        }
        return new FicheiroExportacaoAT($"AT_{d.EmitenteNuit}_{d.Ano:0000}{d.Mes:00}.zip", zip.ToArray());
    }

    private static void Escrever(ZipArchive arquivo, string nome, string conteudo, DateTime data)
    {
        var entrada = arquivo.CreateEntry(nome, CompressionLevel.Optimal);
        entrada.LastWriteTime = new DateTimeOffset(DateTime.SpecifyKind(data, DateTimeKind.Utc));
        using var s = entrada.Open();
        var bytes = Utf8.GetBytes(conteudo);
        s.Write(bytes);
    }

    private static string Documentos(IReadOnlyList<Documento> docs)
    {
        var sb = new StringBuilder();
        Linha(sb, "tipo", "numero", "estado", "data", "emitido_em_utc", "vencimento", "cliente_nuit", "cliente_nome", "cliente_endereco",
            "moeda", "subtotal", "desconto_global", "base_tributavel", "total_iva", "total", "documento_origem", "motivo_correccao",
            "metodo_pagamento", "referencia_pagamento", "emitido_por", "anulado_em_utc", "motivo_anulacao", "hash", "hash_anterior", "hash_curto");
        foreach (var x in docs)
            Linha(sb, x.TipoDoc.ToString(), x.NumeroFormatado, x.Estado == EstadoDocumento.Anulado ? "A" : "N",
                Data(x.Data), DataHora(x.EmitidoEm), x.Vencimento is { } v ? Data(v) : null, x.ClienteNuit, x.ClienteNome,
                string.Join(", ", new[] { x.ClienteEndereco, x.ClienteCidade }.Where(s => !string.IsNullOrWhiteSpace(s))),
                x.Moeda, Valor(x.Subtotal), Valor(x.DescontoGlobal), Valor(x.BaseTributavel), Valor(x.TotalIva), Valor(x.Total),
                x.DocOrigem?.NumeroFormatado, x.MotivoCorreccao, x.MetodoPagamento?.ToString(), x.ReferenciaPagamento, x.EmitidoPor,
                DataHora(x.AnuladoEm), x.MotivoAnulacao, x.Hash, x.HashAnterior, HashDocumento.Curto(x.Hash));
        return sb.ToString();
    }

    private static string Linhas(IReadOnlyList<Documento> docs)
    {
        var sb = new StringBuilder();
        Linha(sb, "numero", "linha", "referencia", "descricao", "quantidade", "unidade", "preco_unitario", "desconto_pct",
            "base", "codigo_taxa", "taxa_iva_pct", "iva", "motivo_isencao", "total_sem_iva");
        foreach (var x in docs)
            foreach (var l in x.Linhas.OrderBy(l => l.Ordem))
                Linha(sb, x.NumeroFormatado, l.Ordem.ToString(Inv), l.Ref, l.Descricao, l.Quantidade.ToString("0.####", Inv), l.Unidade,
                    Valor(l.PrecoUnitario, "0.00####"), Valor(l.DescontoPct), Valor(l.Base), l.CodigoTaxaIva, Valor(l.TaxaIvaPct), Valor(l.Iva),
                    l.MotivoIsencao, Valor(l.TotalLinha));
        return sb.ToString();
    }

    private static string Iva(IReadOnlyList<Documento> docs)
    {
        var sb = new StringBuilder();
        Linha(sb, "numero", "taxa_iva_pct", "base", "iva", "motivo_isencao");
        foreach (var x in docs)
            foreach (var r in x.IvaResumo.OrderByDescending(r => r.TaxaPct))
                Linha(sb, x.NumeroFormatado, Valor(r.TaxaPct), Valor(r.Base), Valor(r.Iva), r.MotivoIsencao);
        return sb.ToString();
    }

    private static string LeiaMe(DadosExportacaoAT d, IReadOnlyList<Documento> docs, (string Nome, string Conteudo)[] ficheiros)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Exportação mensal de documentos fiscais — FORMATO PROVISÓRIO (csv-provisorio-v1)");
        sb.AppendLine("Aguarda a estrutura oficial da AT (Aviso n.º 40/AT/DGI/2025). Não submeter enquanto a AT não confirmar o formato.");
        sb.AppendLine();
        sb.AppendLine($"Período:        {d.Mes:00}/{d.Ano}");
        sb.AppendLine($"Emitente:       {d.EmitenteNome} (NUIT {d.EmitenteNuit})");
        sb.AppendLine($"Software:       {d.Software}{(string.IsNullOrWhiteSpace(d.NumeroCertificado) ? "" : $" — certificado n.º {d.NumeroCertificado}/AT")}");
        sb.AppendLine($"Gerado em:      {d.GeradoEmUtc:yyyy-MM-ddTHH:mm:ss}Z");
        sb.AppendLine();
        sb.AppendLine("Ficheiros (UTF-8, separador vírgula, ponto decimal, datas ISO 8601, valores em MZN):");
        foreach (var (nome, conteudo) in ficheiros)
            sb.AppendLine($"  {nome,-16} SHA-256 {Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(conteudo)))}");
        sb.AppendLine();
        sb.AppendLine("Totais de controlo (documentos não anulados):");
        sb.AppendLine($"  {"Tipo",-6}{"Docs",6}{"Base tributável",20}{"IVA",18}{"Total",20}");
        foreach (var g in docs.Where(x => x.Estado != EstadoDocumento.Anulado).GroupBy(x => x.TipoDoc).OrderBy(g => g.Key))
            sb.AppendLine($"  {g.Key,-6}{g.Count(),6}{Valor(g.Sum(x => x.BaseTributavel)),20}{Valor(g.Sum(x => x.TotalIva)),18}{Valor(g.Sum(x => x.Total)),20}");
        sb.AppendLine($"  Anulados no período: {docs.Count(x => x.Estado == EstadoDocumento.Anulado)} (incluídos nos CSV com estado \"A\").");
        sb.AppendLine();
        sb.AppendLine("Estado: N = normal, A = anulado. O hash encadeia os documentos de cada série (SHA-256 de data; data/hora de emissão;");
        sb.AppendLine("número; total; hash anterior) e o hash curto é o impresso no rodapé do documento.");
        return sb.ToString();
    }

    private static string Data(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
    private static string? DataHora(DateTime? d) => d is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", Inv) : null;
    private static string Valor(decimal v, string formato = "0.00") => v.ToString(formato, Inv);

    private static void Linha(StringBuilder sb, params string?[] campos)
    {
        for (var i = 0; i < campos.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var c = campos[i] ?? "";
            if (c.IndexOfAny([',', '"', '\n', '\r']) >= 0) sb.Append('"').Append(c.Replace("\"", "\"\"")).Append('"');
            else sb.Append(c);
        }
        sb.Append("\r\n");
    }
}
