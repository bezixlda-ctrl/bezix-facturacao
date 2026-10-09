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
public sealed class GeradorPdfQuestPdf(IArmazenamentoFicheiros ficheiros, Microsoft.Extensions.Options.IOptions<OpcoesCertificacao> certificacao) : IGeradorPdf
{

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
                page.Content().PaddingTop(14).Element(c =>
                {
                    if (doc.TipoDoc == TipoDocumento.RC) ConteudoRecibo(c, doc, emitente, moeda, opcoes, cor);
                    else Conteudo(c, doc, emitente, moeda, opcoes, cor);
                });
                page.Footer().Element(c => Rodape(c, doc, emitente, opcoes, certificacao.Value.TextoRodape));

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

    /// <summary>Recibo: quantia recebida, por extenso, método e documentos pagos.</summary>
    private static void ConteudoRecibo(IContainer c, Documento doc, DadosEmitente e, Moeda moeda, OpcoesPdf opcoes, Color cor)
    {
        c.Column(col =>
        {
            col.Item().Text(doc.NumeroFormatado is null ? "Recibo" : $"Recibo n.º {doc.NumeroFormatado}").Bold().FontSize(14).FontColor(cor);
            col.Item().PaddingTop(6).Text(t =>
            {
                t.Span("Recebemos de ");
                t.Span(doc.ClienteNome).SemiBold();
                t.Span($" (NUIT {doc.ClienteNuit ?? "—"}) a quantia de ");
                t.Span($"{N(doc.Total)} MZN").Bold();
                t.Span($" ({PorExtenso.Valor(doc.Total, moeda.NomeSingular, moeda.NomePlural, moeda.CentimoSingular, moeda.CentimoPlural)})");
                t.Span(doc.MetodoPagamento is { } m ? $", por {MetodoPagamentoInfo.Nome(m).ToLowerInvariant()}" : "");
                t.Span(doc.ReferenciaPagamento is null ? "" : $" (ref. {doc.ReferenciaPagamento})");
                t.Span($", em {D(doc.Data)}.");
            });

            if (opcoes.Alocacoes.Count > 0)
            {
                col.Item().PaddingTop(12).Text("Para pagamento de:").SemiBold();
                col.Item().PaddingTop(4).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(1); cd.RelativeColumn(1.4f); cd.RelativeColumn(1.4f); });
                    t.Header(h =>
                    {
                        IContainer Cab(IContainer x) => x.Background(cor).PaddingVertical(5).PaddingHorizontal(4);
                        h.Cell().Element(Cab).Text("Documento").FontColor(Colors.White).SemiBold();
                        h.Cell().Element(Cab).Text("Data").FontColor(Colors.White).SemiBold();
                        h.Cell().Element(Cab).AlignRight().Text("Valor do documento").FontColor(Colors.White).SemiBold();
                        h.Cell().Element(Cab).AlignRight().Text("Valor pago").FontColor(Colors.White).SemiBold();
                    });
                    foreach (var a in opcoes.Alocacoes)
                    {
                        IContainer Cel(IContainer x) => x.BorderBottom(0.5f).BorderColor("#E2E7EE").PaddingVertical(4).PaddingHorizontal(4);
                        t.Cell().Element(Cel).Text(a.Documento);
                        t.Cell().Element(Cel).Text(D(a.Data));
                        t.Cell().Element(Cel).AlignRight().Text(N(a.TotalMzn));
                        t.Cell().Element(Cel).AlignRight().Text(N(a.Valor));
                    }
                });
            }

            col.Item().PaddingTop(10).AlignRight().Width(240).Column(dir =>
            {
                if (opcoes.Adiantamento > 0)
                {
                    dir.Item().BorderBottom(0.5f).BorderColor("#E2E7EE").PaddingVertical(3).Row(r =>
                    {
                        r.RelativeItem().Text("Alocado a documentos");
                        r.AutoItem().Text(N(doc.Total - opcoes.Adiantamento));
                    });
                    dir.Item().BorderBottom(0.5f).BorderColor("#E2E7EE").PaddingVertical(3).Row(r =>
                    {
                        r.RelativeItem().Text("Adiantamento (crédito do cliente)");
                        r.AutoItem().Text(N(opcoes.Adiantamento));
                    });
                }
                dir.Item().PaddingTop(4).Background(cor).Padding(6).Row(r =>
                {
                    r.RelativeItem().Text("TOTAL RECEBIDO (MZN)").Bold().FontColor(Colors.White).FontSize(11);
                    r.AutoItem().Text(N(doc.Total)).Bold().FontColor(Colors.White).FontSize(11);
                });
            });

            if (!string.IsNullOrWhiteSpace(doc.Observacoes))
                col.Item().PaddingTop(8).Text(t => { t.Span("Observações: ").SemiBold(); t.Span(doc.Observacoes); });
            if (doc.Estado == EstadoDocumento.Anulado)
                col.Item().PaddingTop(10).Text($"Recibo anulado em {doc.AnuladoEm:dd/MM/yyyy}. Motivo: {doc.MotivoAnulacao}").FontColor("#C8372D").SemiBold();
        });
    }

    public byte[] GerarExtracto(Application.Recebimentos.Extracto x, DadosEmitente emitente, string? utilizador)
    {
        var cor = Color.FromHex(emitente.CorPrimaria);
        var logo = CarregarLogotipo(emitente.LogoPath);
        var cabecalho = new Documento
        {
            TipoDoc = TipoDocumento.FT, ClienteId = x.ClienteId, ClienteNome = x.ClienteNome, ClienteNuit = x.ClienteNuit,
        };

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(36);
                page.MarginVertical(32);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor("#1B2430"));
                page.Header().Element(c => Cabecalho(c, cabecalho, emitente, logo, cor));
                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Item().Text("Extracto de conta corrente").Bold().FontSize(14).FontColor(cor);
                    col.Item().Text($"Período de {D(x.De)} a {D(x.Ate)} · valores em MZN").FontColor("#5B6676");

                    col.Item().PaddingTop(10).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(58); cd.ConstantColumn(92); cd.RelativeColumn(); cd.ConstantColumn(72); cd.ConstantColumn(72); cd.ConstantColumn(78);
                        });
                        t.Header(h =>
                        {
                            IContainer Cab(IContainer c) => c.Background(cor).PaddingVertical(5).PaddingHorizontal(4);
                            h.Cell().Element(Cab).Text("Data").FontColor(Colors.White).SemiBold();
                            h.Cell().Element(Cab).Text("Documento").FontColor(Colors.White).SemiBold();
                            h.Cell().Element(Cab).Text("Descrição").FontColor(Colors.White).SemiBold();
                            h.Cell().Element(Cab).AlignRight().Text("Débito").FontColor(Colors.White).SemiBold();
                            h.Cell().Element(Cab).AlignRight().Text("Crédito").FontColor(Colors.White).SemiBold();
                            h.Cell().Element(Cab).AlignRight().Text("Saldo").FontColor(Colors.White).SemiBold();
                        });
                        IContainer Cel(IContainer c) => c.BorderBottom(0.5f).BorderColor("#E2E7EE").PaddingVertical(4).PaddingHorizontal(4);
                        t.Cell().ColumnSpan(5).Element(Cel).Text("Saldo anterior").SemiBold();
                        t.Cell().Element(Cel).AlignRight().Text(N(x.SaldoInicial)).SemiBold();
                        foreach (var m in x.Movimentos)
                        {
                            t.Cell().Element(Cel).Text(D(m.Data));
                            t.Cell().Element(Cel).Text(m.Documento);
                            t.Cell().Element(Cel).Text(m.Descricao);
                            t.Cell().Element(Cel).AlignRight().Text(m.Debito == 0 ? "" : N(m.Debito));
                            t.Cell().Element(Cel).AlignRight().Text(m.Credito == 0 ? "" : N(m.Credito));
                            t.Cell().Element(Cel).AlignRight().Text(N(m.Saldo));
                        }
                        t.Cell().ColumnSpan(3).Element(Cel).AlignRight().Text("Totais do período").SemiBold();
                        t.Cell().Element(Cel).AlignRight().Text(N(x.TotalDebitos)).SemiBold();
                        t.Cell().Element(Cel).AlignRight().Text(N(x.TotalCreditos)).SemiBold();
                        t.Cell().Element(Cel).Text("");
                    });

                    col.Item().PaddingTop(10).AlignRight().Width(240).Background(cor).Padding(6).Row(r =>
                    {
                        r.RelativeItem().Text($"SALDO EM {D(x.Ate)}").Bold().FontColor(Colors.White).FontSize(11);
                        r.AutoItem().Text(N(x.SaldoFinal)).Bold().FontColor(Colors.White).FontSize(11);
                    });
                    col.Item().PaddingTop(4).AlignRight().Text(x.SaldoFinal > 0 ? "Saldo a nosso favor" : x.SaldoFinal < 0 ? "Saldo a seu favor" : "Conta saldada")
                        .FontSize(8).FontColor("#5B6676");

                    if (emitente.Contas.Count > 0 && x.SaldoFinal > 0)
                        col.Item().PaddingTop(14).Column(c =>
                        {
                            c.Item().Text("Pagamento por transferência").SemiBold().FontColor(cor);
                            foreach (var conta in emitente.Contas)
                                c.Item().Text(string.Join("   ", new[] { conta.Banco, $"Conta {conta.NumeroConta}", conta.Nib is null ? null : $"NIB {conta.Nib}" }.Where(v => v is not null))).FontSize(8.5f);
                        });
                });
                page.Footer().Element(c => Rodape(c, new Documento(), emitente, new OpcoesPdf(MarcaPdf.Nenhuma, utilizador), certificacao.Value.TextoRodape));
            });
        }).GeneratePdf();
    }

    private static void Rodape(IContainer c, Documento doc, DadosEmitente e, OpcoesPdf opcoes, string software)
    {
        c.BorderTop(0.75f).BorderColor("#D5DBE3").PaddingTop(5).Column(col =>
        {
            col.Item().Text(e.RodapeTexto).FontSize(7.5f).FontColor("#5B6676");
            col.Item().Row(r =>
            {
                var hash = HashDocumento.Curto(doc.Hash);
                var partes = new List<string> { software };
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
