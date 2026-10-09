using System.Security.Cryptography;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Fiscal;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bezix.Facturacao.Application.Fiscal;

public sealed record EstadoSerieIntegridade(int SerieId, TipoDocumento Tipo, string Serie, int Documentos, int? PrimeiroComProblema);

/// <summary>
/// Exportação mensal para a AT: só documentos fiscais (FT, FR, FG, NC, ND, RC) emitidos no mês, incluindo os anulados.
/// Antes de gerar, verifica a cadeia de hashes de cada série: com uma cadeia quebrada a exportação é recusada.
/// </summary>
public sealed class ServicoExportacaoAT(
    IAppDbContext db,
    IExportadorAT exportador,
    IArmazenamentoFicheiros ficheiros,
    ServicoEmpresa empresas,
    IUtilizadorActual utilizador,
    IServicoAuditoria auditoria,
    IOptions<OpcoesCertificacao> certificacao,
    TimeProvider relogio)
{
    public const string Pasta = "exportacoes-at";

    public static readonly TipoDocumento[] TiposFiscais = Enum.GetValues<TipoDocumento>().Where(TipoDocumentoInfo.TemValorFiscal).ToArray();

    public DateOnly Hoje() => DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime.AddHours(2));

    public async Task<(Resultado Resultado, int? Id)> GerarAsync(int ano, int mes, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Relatorios.Fiscal)) return (Resultado.Falha("O seu perfil não permite exportar para a AT."), null);
        if (mes is < 1 or > 12 || ano < 2000) return (Resultado.Falha("Período inválido."), null);
        var inicio = new DateOnly(ano, mes, 1);
        if (inicio > Hoje()) return (Resultado.Falha("Não se exporta um mês que ainda não começou."), null);
        var fim = inicio.AddMonths(1).AddDays(-1);

        var empresa = await empresas.ObterAsync(ct);
        var docs = await db.Documentos.AsNoTracking()
            .Where(d => TiposFiscais.Contains(d.TipoDoc) && (d.Estado == EstadoDocumento.Emitido || d.Estado == EstadoDocumento.Anulado)
                && d.Data >= inicio && d.Data <= fim)
            .Include(d => d.Linhas).Include(d => d.IvaResumo).Include(d => d.DocOrigem)
            .ToListAsync(ct);

        var quebradas = (await VerificarIntegridadeAsync(docs.Select(d => d.SerieId).Distinct().ToList(), ct))
            .Where(s => s.PrimeiroComProblema is not null).ToList();
        if (quebradas.Count > 0)
        {
            auditoria.Registar(nameof(ExportacaoAT), null, "Integridade.Falha", null,
                quebradas.Select(q => new { q.Serie, q.PrimeiroComProblema }).ToList());
            await db.SaveChangesAsync(ct);
            return (Resultado.Falha(quebradas.Select(q =>
                $"A cadeia de integridade da série {q.Serie} está quebrada no documento n.º {q.PrimeiroComProblema}. A exportação foi bloqueada; contacte o suporte.").ToArray()), null);
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        var c = certificacao.Value;
        var ficheiro = exportador.Gerar(new DadosExportacaoAT(ano, mes, empresa.Nome, empresa.Nuit, c.Identificacao, c.NumeroCertificado, agora, docs));
        var caminho = await ficheiros.GuardarAsync(Pasta, ".zip", ficheiro.Conteudo, ct);
        var validos = docs.Where(d => d.Estado == EstadoDocumento.Emitido).ToList();
        var e = new ExportacaoAT
        {
            Ano = ano, Mes = mes, Formato = exportador.Formato, NomeFicheiro = ficheiro.NomeFicheiro, FicheiroPath = caminho,
            HashSha256 = Convert.ToHexString(SHA256.HashData(ficheiro.Conteudo)), Tamanho = ficheiro.Conteudo.LongLength,
            NumDocs = docs.Count, NumAnulados = docs.Count - validos.Count,
            // NC reduzem a base e o IVA do mês; RC não têm IVA próprio (o IVA está na factura).
            TotalBase = validos.Where(d => d.TipoDoc != TipoDocumento.RC).Sum(d => d.TipoDoc == TipoDocumento.NC ? -d.BaseTributavel : d.BaseTributavel),
            TotalIva = validos.Where(d => d.TipoDoc != TipoDocumento.RC).Sum(d => d.TipoDoc == TipoDocumento.NC ? -d.TotalIva : d.TotalIva),
            GeradoEm = agora, GeradoPor = utilizador.Email ?? "",
        };
        db.ExportacoesAT.Add(e);
        await db.SaveChangesAsync(ct);
        auditoria.Registar(nameof(ExportacaoAT), e.Id.ToString(), "ExportacaoAT.Gerar", null,
            new { e.Periodo, e.Formato, e.NumDocs, e.NumAnulados, e.TotalBase, e.TotalIva, e.HashSha256 });
        await db.SaveChangesAsync(ct);
        return (Resultado.Ok(), e.Id);
    }

    public Task<List<ExportacaoAT>> ListarAsync(CancellationToken ct = default) =>
        db.ExportacoesAT.AsNoTracking().OrderByDescending(e => e.Ano).ThenByDescending(e => e.Mes).ThenByDescending(e => e.Id).Take(120).ToListAsync(ct);

    public async Task<(Stream Conteudo, string Nome)?> FicheiroAsync(int id, CancellationToken ct = default)
    {
        var e = await db.ExportacoesAT.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null || ficheiros.Abrir(e.FicheiroPath) is not { } s) return null;
        auditoria.Registar(nameof(ExportacaoAT), id.ToString(), "ExportacaoAT.Descarregar");
        await db.SaveChangesAsync(ct);
        return (s, e.NomeFicheiro);
    }

    /// <summary>Regista que o ficheiro foi submetido no e-Declaração (com a referência do comprovativo).</summary>
    public async Task<Resultado> MarcarSubmetidaAsync(int id, string? referencia, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Relatorios.Fiscal)) return Resultado.Falha("O seu perfil não permite exportar para a AT.");
        var e = await db.ExportacoesAT.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return Resultado.Falha("Exportação não encontrada.");
        if (e.SubmetidoEm is not null) return Resultado.Falha("Esta exportação já está marcada como submetida.");
        e.SubmetidoEm = relogio.GetUtcNow().UtcDateTime;
        e.SubmetidoPor = utilizador.Email;
        e.ReferenciaSubmissao = string.IsNullOrWhiteSpace(referencia) ? null : referencia.Trim()[..Math.Min(100, referencia.Trim().Length)];
        auditoria.Registar(nameof(ExportacaoAT), id.ToString(), "ExportacaoAT.Submeter", null, new { e.Periodo, e.ReferenciaSubmissao });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>O mês anterior, se teve documentos fiscais e ainda não foi exportado (alerta no painel).</summary>
    public async Task<(int Ano, int Mes)?> PorFazerAsync(CancellationToken ct = default)
    {
        var anterior = new DateOnly(Hoje().Year, Hoje().Month, 1).AddMonths(-1);
        var fim = anterior.AddMonths(1).AddDays(-1);
        if (await db.ExportacoesAT.AnyAsync(e => e.Ano == anterior.Year && e.Mes == anterior.Month, ct)) return null;
        var teve = await db.Documentos.AnyAsync(d => TiposFiscais.Contains(d.TipoDoc) && d.Estado != EstadoDocumento.Rascunho
            && d.Estado != EstadoDocumento.AguardaAprovacao && d.Data >= anterior && d.Data <= fim, ct);
        return teve ? (anterior.Year, anterior.Month) : null;
    }

    /// <summary>Verifica a cadeia de hashes das séries indicadas (ou de todas).</summary>
    public async Task<List<EstadoSerieIntegridade>> VerificarIntegridadeAsync(IReadOnlyCollection<int>? series = null, CancellationToken ct = default)
    {
        var q = db.Series.AsNoTracking();
        if (series is not null) q = q.Where(s => series.Contains(s.Id));
        var lista = await q.OrderBy(s => s.TipoDoc).ThenBy(s => s.Ano).ThenBy(s => s.Codigo).ToListAsync(ct);
        var resultado = new List<EstadoSerieIntegridade>();
        foreach (var s in lista)
        {
            var docs = await db.Documentos.AsNoTracking()
                .Where(d => d.SerieId == s.Id && d.Numero != null)
                .OrderBy(d => d.Numero).ToListAsync(ct);
            var problema = HashDocumento.VerificarCadeia(docs);
            // Saltos de numeração também são falhas de integridade.
            problema ??= docs.Select((d, i) => (d.Numero, Esperado: i + 1)).FirstOrDefault(x => x.Numero != x.Esperado).Numero;
            resultado.Add(new EstadoSerieIntegridade(s.Id, s.TipoDoc, $"{s.TipoDoc} {s.Codigo}{s.Ano}", docs.Count, problema));
        }
        return resultado;
    }
}
