using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Documentos;

public sealed record FicheiroPdf(byte[] Conteudo, string Nome);

/// <summary>
/// Um único gerador produz o PDF; imprimir, descarregar e enviar por email usam o mesmo ficheiro.
/// O PDF original é gerado e guardado na emissão e nunca muda. A 2.ª via e os anulados regeneram-se
/// a partir dos dados congelados no documento, com a marca correspondente.
/// </summary>
public sealed class ServicoPdfDocumentos(
    IAppDbContext db,
    IGeradorPdf gerador,
    IArmazenamentoFicheiros ficheiros,
    ServicoEmpresa empresas,
    IUtilizadorActual utilizador)
{
    public async Task<FicheiroPdf?> ObterAsync(int id, bool segundaVia = false, CancellationToken ct = default)
    {
        var doc = await CarregarAsync(id, ct);
        if (doc is null) return null;

        if (doc.Estado == EstadoDocumento.Anulado)
            return new(await GerarAsync(doc, MarcaPdf.Anulado, ct), doc.NomeFicheiroPdf());
        if (!doc.Emitido)
            return new(await GerarAsync(doc, MarcaPdf.Rascunho, ct), doc.NomeFicheiroPdf());
        if (segundaVia)
            return new(await GerarAsync(doc, MarcaPdf.SegundaVia, ct), doc.NomeFicheiroPdf());

        return new(await GarantirPdfGuardadoAsync(id, ct) ?? [], doc.NomeFicheiroPdf());
    }

    /// <summary>Devolve o PDF original guardado; se ainda não existir (ex.: falha depois da emissão), gera-o e guarda-o.</summary>
    public async Task<byte[]?> GarantirPdfGuardadoAsync(int id, CancellationToken ct = default)
    {
        var doc = await CarregarAsync(id, ct);
        if (doc is null || doc.Estado != EstadoDocumento.Emitido) return null;

        if (doc.PdfPath is not null)
        {
            await using var stream = ficheiros.Abrir(doc.PdfPath);
            if (stream is not null)
            {
                using var memoria = new MemoryStream();
                await stream.CopyToAsync(memoria, ct);
                return memoria.ToArray();
            }
        }

        var bytes = await GerarAsync(doc, MarcaPdf.Nenhuma, ct);
        var caminho = await ficheiros.GuardarAsync("documentos", ".pdf", bytes, ct);
        var tracked = await db.Documentos.SingleAsync(d => d.Id == id, ct);
        tracked.PdfPath = caminho;
        await db.SaveChangesAsync(ct);
        return bytes;
    }

    private async Task<byte[]> GerarAsync(Documento doc, MarcaPdf marca, CancellationToken ct)
    {
        var emitente = DadosEmitente.DeJson(doc.EmitenteJson)
            ?? DadosEmitente.De(await empresas.ObterAsync(ct), await db.ContasBancarias.AsNoTracking().ToListAsync(ct));
        var moeda = await db.Moedas.AsNoTracking().SingleOrDefaultAsync(m => m.Codigo == doc.Moeda, ct)
            ?? new Moeda { Codigo = doc.Moeda, Nome = doc.Moeda, Simbolo = doc.Moeda, NomeSingular = doc.Moeda, NomePlural = doc.Moeda };
        // O utilizador impresso no rodapé é quem emitiu (não quem reimprime), para o PDF ser sempre igual.
        var quem = doc.Emitido ? doc.EmitidoPor : utilizador.Email;
        return gerador.GerarDocumento(doc, emitente, moeda, new OpcoesPdf(marca, quem));
    }

    private Task<Documento?> CarregarAsync(int id, CancellationToken ct) =>
        db.Documentos.AsNoTracking()
            .Include(d => d.Linhas.OrderBy(l => l.Ordem))
            .Include(d => d.IvaResumo)
            .Include(d => d.DocOrigem)
            .SingleOrDefaultAsync(d => d.Id == id, ct);
}
