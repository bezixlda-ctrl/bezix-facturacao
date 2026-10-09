using System.Globalization;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Comum;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Bezix.Facturacao.Infrastructure.Pdf;

/// <summary>
/// PDF A4 dos documentos, baseado na factura modelo n.º 08/26: logótipo e emitente à esquerda, cliente à direita,
/// linhas com cabeçalho repetido em cada página, totais com IVA por taxa, contas bancárias e rodapé
/// "Processado por computador" com identificação do software, hash curto, utilizador e página X/Y.
/// </summary>
public sealed class GeradorPdfQuestPdf(IArmazenamentoFicheiros ficheiros) : IGeradorPdf
{
    public const string IdentificacaoSoftware = "Bezix Facturação v1";

    static GeradorPdfQuestPdf()
    {
        // Licença Community: gratuita para empresas com receita anual inferior a 1 M USD (confirmado pela Bezix).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static readonly NumberFormatInfo FormatoMz = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NumberGroupSizes = [3] };
    private static string N(decimal v) => v.ToString("#,##0.00", FormatoMz);
    private static string Q(decimal v) => v.ToString("#,##0.####", FormatoMz);
    private static string P(decimal v) => v.ToString("0.##", FormatoMz);
    private static string D(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public byte[] GerarDocumento(Documento doc, DadosEmitente emitente, Moeda moeda, OpcoesPdf opcoes)
    {
        var cor = Color.FromHex(emitente.CorPrimaria);
        var logo = CarregarLogotipo(emitente.LogoPath);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(36);
                page.MarginVertical(32);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor("#1B2430"));

                page.Header().Element(c => Cabecalho(c, doc, emitente, logo, cor));
                page.Content().PaddingTop(14).Element(c => Conteudo(c, doc, emitente, moeda, opcoes, cor));
                page.Footer().Element(c => Rodape(c, doc, emitente, opcoes));

                var marca = opcoes.Marca switch
                {
                    MarcaPdf.Anulado => "ANULADO",
                    MarcaPdf.SegundaVia => "2ª VIA",
                    MarcaPdf.Rascunho => "RASCUNHO",
                    _ => null,
                };
                if (marca is not null)
                {
                    page.Foreground().AlignCenter().AlignMiddle().Rotate(-30)
                        .Text(marca).FontSize(marca.Length > 6 ? 90 : 110).Bold()
                        .FontColor(opcoes.Marca == MarcaPdf.Anulado ? "#40C8372D" : "#300F172A");
                }
            });
        }).GeneratePdf();
    }

    private byte[]? CarregarLogotipo(string? caminho)
    {
        if (caminho is null) return null;
        using var s = ficheiros.Abrir(caminho);
        if (s is null) return null;
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    private static void Cabecalho(IContainer c, Documento doc, DadosEmitente e, byte[]? logo, Color cor)
    {
        c.Row(row =>
        {
            row.RelativeItem(55).Column(col =>
            {
                if (logo is not null) col.Item().Height(54).AlignLeft().Image(logo).FitArea();
                if (!string.IsNullOrWhiteSpace(e.Slogan)) col.Item().PaddingTop(2).Text(e.Slogan).Italic().FontSize(8).FontColor("#5B6676");
                col.Item().PaddingTop(4).Text(e.Nome).Bold().FontSize(11).FontColor(cor);
                if (!string.IsNullOrWhiteSpace(e.Provincia)) col.Item().Text(e.Provincia);
                col.Item().Text(string.Join(", ", new[] { e.Endereco, e.Cidade }.Where(x => !string.IsNullOrWhiteSpace(x))));
                col.Item().Text($"NUIT: {e.Nuit}").SemiBold();
                if (!string.IsNullOrWhiteSpace(e.Telefones)) col.Item().Text($"Tel.: {e.Telefones}");
                var web = string.Join("   ", new[] { e.Email, e.Site }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (web.Length > 0) col.Item().Text(web);
            });

            row.RelativeItem(45).PaddingLeft(12).Border(0.75f).BorderColor("#D5DBE3").Padding(10).Column(col =>
            {
                col.Item().Text("Exmo(a). Sr(a).").FontSize(8).FontColor("#5B6676");
                col.Item().PaddingTop(2).Text(doc.ClienteNome).Bold().FontSize(10.5f);
                if (!string.IsNullOrWhiteSpace(doc.ClienteEndereco)) col.Item().Text(doc.ClienteEndereco);
                if (!string.IsNullOrWhiteSpace(doc.ClienteCidade)) col.Item().Text(doc.ClienteCidade);
                col.Item().PaddingTop(3).Text($"NUIT: {doc.ClienteNuit ?? "—"}").SemiBold();
                if (!string.IsNullOrWhiteSpace(doc.ClienteCelular)) col.Item().Text($"Celular: {doc.ClienteCelular}");
                if (!string.IsNullOrWhiteSpace(doc.ClienteTelefone)) col.Item().Text($"Telefone: {doc.ClienteTelefone}");
                if (!string.IsNullOrWhiteSpace(doc.ClienteContacto)) col.Item().Text($"Att.: {doc.ClienteContacto}");
            });
        });
    }

    private static void Conteudo(IContainer c, Documento doc, DadosEmitente e, Moeda moeda, OpcoesPdf opcoes, Color cor)
    {
        c.Column(col =>
        {
            // Título e dados do documento
            var titulo = TipoDocumentoInfo.Nome(doc.TipoDoc) + (doc.NumeroFormatado is null ? " (rascunho)" : $" n.º {doc.NumeroFormatado}");
            col.Item().Text(titulo).Bold().FontSize(14).FontColor(cor);
            if (RegrasTipo.MencaoObrigatoria(doc.TipoDoc) is { } mencao)
                col.Item().Text(mencao).Bold().FontSize(9).FontColor("#C8372D");

            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.RelativeColumn(); cd.RelativeColumn(); cd.RelativeColumn(); });
                void Celula(string rotulo, string valor) => t.Cell().Background("#F3F5F8").Padding(5).Column(x =>
                {
                    x.Item().Text(rotulo).FontSize(7.5f).FontColor("#5B6676");
                    x.Item().Text(valor).SemiBold();
                });
                Celula("Data", D(doc.Data));
                Celula(RegrasTipo.TemVencimento(doc.TipoDoc) ? "Vencimento" : "Documento", RegrasTipo.TemVencimento(doc.TipoDoc) ? (doc.Vencimento is { } v ? D(v) : "—") : doc.TipoDoc.ToString());
                Celula("Cliente n.º", doc.ClienteId.ToString(CultureInfo.InvariantCulture));
                Celula("Moeda", doc.EmMoedaEstrangeira ? $"{doc.Moeda} (1 {doc.Moeda} = {doc.Cambio.ToString("#,##0.######", FormatoMz)} MZN)" : doc.Moeda);
            });

            if (doc.DocOrigem is { } origem)
                col.Item().PaddingTop(6).Text(t =>
                {
                    t.Span("Referente a: ").SemiBold();
                    t.Span($"{origem.NumeroFormatado} de {D(origem.Data)}");
                    if (!string.IsNullOrWhiteSpace(doc.MotivoCorreccao)) { t.Span("   Motivo: ").SemiBold(); t.Span(doc.MotivoCorreccao); }
                });

            if (doc.TipoDoc == TipoDocumento.GR)
                col.Item().PaddingTop(6).Text(t =>
                {
                    t.Span("Carga: ").SemiBold(); t.Span(doc.LocalCarga ?? "—");
                    t.Span("   Descarga: ").SemiBold(); t.Span(doc.LocalDescarga ?? "—");
                    t.Span("   Matrícula: ").SemiBold(); t.Span(doc.Matricula ?? "—");
                });

            // Linhas
            col.Item().PaddingTop(10).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(40);   // Qt
                    cd.ConstantColumn(58);   // Ref
                    cd.RelativeColumn();     // Descrição
                    cd.ConstantColumn(70);   // Preço
                    cd.ConstantColumn(38);   // Desc
                    cd.ConstantColumn(34);   // IVA
                    cd.ConstantColumn(78);   // Total
                });
                t.Header(h =>
                {
                    IContainer Cab(IContainer x) => x.Background(cor).PaddingVertical(5).PaddingHorizontal(4);
                    h.Cell().Element(Cab).AlignRight().Text("Qt").FontColor(Colors.White).SemiBold();
                    h.Cell().Element(Cab).Text("Ref").FontColor(Colors.White).SemiBold();
                    h.Cell().Element(Cab).Text("Descrição").FontColor(Colors.White).SemiBold();
                    h.Cell().Element(Cab).AlignRight().Text("Preço unit.").FontColor(Colors.White).SemiBold();
                    h.Cell().Element(Cab).AlignRight().Text("Desc.%").FontColor(Colors.White).SemiBold();
                    h.Cell().Element(Cab).AlignRight().Text("IVA%").FontColor(Colors.White).SemiBold();
                    h.Cell().Element(Cab).AlignRight().Text("Total líquido").FontColor(Colors.White).SemiBold();
                });
                foreach (var l in doc.Linhas.OrderBy(x => x.Ordem))
                {
                    IContainer Cel(IContainer x) => x.BorderBottom(0.5f).BorderColor("#E2E7EE").PaddingVertical(4).PaddingHorizontal(4);
                    t.Cell().Element(Cel).AlignRight().Text(Q(l.Quantidade) + (l.Unidade is { Length: > 0 } u && u != "un" ? " " + u : ""));
                    t.Cell().Element(Cel).Text(l.Ref ?? "");
                    t.Cell().Element(Cel).Text(l.Descricao);
                    t.Cell().Element(Cel).AlignRight().Text(N(l.PrecoUnitario));
                    t.Cell().Element(Cel).AlignRight().Text(l.DescontoPct == 0 ? "" : P(l.DescontoPct));
                    t.Cell().Element(Cel).AlignRight().Text(P(l.TaxaIvaPct));
                    t.Cell().Element(Cel).AlignRight().Text(N(l.TotalLinha));
                }
            });

            // Totais, extenso e contravalor
            col.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem(55).PaddingRight(16).Column(esq =>
                {
                    var motivos = doc.IvaResumo.Where(r => r.TaxaPct == 0 && r.MotivoIsencao != null).Select(r => r.MotivoIsencao!).Distinct().ToList();
                    foreach (var m in motivos) esq.Item().Text($"Isenção de IVA: {m}").FontSize(8);

                    if (opcoes.ValorPorExtenso && doc.Total > 0)
                        esq.Item().PaddingTop(4).Text(t =>
                        {
                            t.Span("São: ").SemiBold();
                            t.Span(PorExtenso.Valor(doc.Total, moeda.NomeSingular, moeda.NomePlural, moeda.CentimoSingular, moeda.CentimoPlural)).Italic();
                        });

                    if (doc.EmMoedaEstrangeira)
                        esq.Item().PaddingTop(6).Background("#F3F5F8").Padding(6).Column(x =>
                        {
                            x.Item().Text($"Contravalor em MZN (câmbio 1 {doc.Moeda} = {doc.Cambio.ToString("#,##0.######", FormatoMz)} MZN)").SemiBold().FontSize(8);
                            x.Item().Text($"Base tributável: {N(doc.BaseTributavelMzn)} MZN   IVA: {N(doc.TotalIvaMzn)} MZN   Total: {N(doc.TotalMzn)} MZN").FontSize(8);
                        });

                    if (doc.TipoDoc == TipoDocumento.FR && doc.MetodoPagamento is { } metodo)
                        esq.Item().PaddingTop(6).Text(t =>
                        {
                            t.Span("Pago por: ").SemiBold();
                            t.Span(MetodoPagamentoInfo.Nome(metodo) + (doc.ReferenciaPagamento is null ? "" : $" (ref. {doc.ReferenciaPagamento})"));
                        });

                    if (!string.IsNullOrWhiteSpace(doc.Observacoes))
                        esq.Item().PaddingTop(6).Text(t => { t.Span("Observações: ").SemiBold(); t.Span(doc.Observacoes); });
                });

                row.RelativeItem(45).Column(dir =>
                {
                    void Linha(string rotulo, string valor, bool destaque = false)
                    {
                        dir.Item().BorderBottom(0.5f).BorderColor("#E2E7EE").PaddingVertical(3).Row(r =>
                        {
                            var a = r.RelativeItem().Text(rotulo);
                            var b = r.AutoItem().Text(valor);
                            if (destaque) { a.Bold(); b.Bold(); }
                        });
                    }
                    Linha("Subtotal", N(doc.Subtotal));
                    if (doc.DescontoGlobal != 0) Linha($"Desconto ({P(doc.DescontoGlobalPct)}%)", "-" + N(doc.DescontoGlobal));
                    Linha("Base tributável", N(doc.BaseTributavel));
                    foreach (var r in doc.IvaResumo.OrderByDescending(r => r.TaxaPct))
                        Linha($"IVA {P(r.TaxaPct)}% sobre {N(r.Base)}", N(r.Iva));
                    dir.Item().PaddingTop(4).Background(cor).Padding(6).Row(r =>
                    {
                        r.RelativeItem().Text($"TOTAL ({doc.Moeda})").Bold().FontColor(Colors.White).FontSize(11);
                        r.AutoItem().Text(N(doc.Total)).Bold().FontColor(Colors.White).FontSize(11);
                    });
                });
            });

            // Contas bancárias (não fazem sentido em guias)
            if (e.Contas.Count > 0 && doc.TipoDoc != TipoDocumento.GR)
                col.Item().PaddingTop(14).Column(x =>
                {
                    x.Item().Text("Pagamento por transferência").SemiBold().FontColor(cor);
                    foreach (var conta in e.Contas)
                    {
                        var partes = new List<string> { conta.Banco, $"Conta {conta.NumeroConta}" };
                        if (conta.Nib is not null) partes.Add($"NIB {conta.Nib}");
                        if (conta.Iban is not null) partes.Add($"IBAN {conta.Iban}");
                        if (conta.Swift is not null) partes.Add($"SWIFT {conta.Swift}");
                        x.Item().Text(string.Join("   ", partes)).FontSize(8.5f);
                    }
                });

            if (doc.Estado == EstadoDocumento.Anulado)
                col.Item().PaddingTop(10).Text($"Documento anulado em {doc.AnuladoEm:dd/MM/yyyy}. Motivo: {doc.MotivoAnulacao}").FontColor("#C8372D").SemiBold();
        });
    }

    private static void Rodape(IContainer c, Documento doc, DadosEmitente e, OpcoesPdf opcoes)
    {
        c.BorderTop(0.75f).BorderColor("#D5DBE3").PaddingTop(5).Column(col =>
        {
            col.Item().Text(e.RodapeTexto).FontSize(7.5f).FontColor("#5B6676");
            col.Item().Row(r =>
            {
                var hash = HashDocumento.Curto(doc.Hash);
                var partes = new List<string> { $"Software: {IdentificacaoSoftware} (certificação AT pendente)" };
                if (hash.Length > 0) partes.Add($"Hash {hash}");
                if (!string.IsNullOrWhiteSpace(opcoes.Utilizador)) partes.Add($"Processado por {opcoes.Utilizador}");
                r.RelativeItem().Text(string.Join(" · ", partes)).FontSize(7).FontColor("#5B6676");
                r.AutoItem().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(7).FontColor("#5B6676"));
                    t.Span("Página ");
                    t.CurrentPageNumber();
                    t.Span("/");
                    t.TotalPages();
                });
            });
        });
    }
}
