using System.Text.RegularExpressions;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Artigos;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Artigos;

public sealed record DadosArtigo(string Codigo, string Descricao, TipoArtigo Tipo, string Unidade, decimal Preco, string CodigoTaxaIva);

public sealed record FiltroArtigos(string? Texto = null, FiltroEstado Estado = FiltroEstado.Activos, int Pagina = 1, int Tamanho = Pagina<object>.TamanhoPadrao);

public sealed partial class ServicoArtigos(IAppDbContext db, IServicoAuditoria auditoria)
{
    public static readonly string[] UnidadesSugeridas = ["un", "h", "dia", "mês", "kg", "m", "m²", "l", "cx", "serviço"];

    public async Task<Pagina<Artigo>> PesquisarAsync(FiltroArtigos f, CancellationToken ct = default)
    {
        var (numero, tamanho) = Pagina<Artigo>.Normalizar(f.Pagina, f.Tamanho);
        var q = db.Artigos.AsNoTracking();
        q = f.Estado switch
        {
            FiltroEstado.Activos => q.Where(a => a.Activo),
            FiltroEstado.Inactivos => q.Where(a => !a.Activo),
            _ => q,
        };
        if (!string.IsNullOrWhiteSpace(f.Texto))
        {
            var t = f.Texto.Trim();
            q = q.Where(a => a.Codigo.Contains(t) || a.Descricao.Contains(t));
        }

        var total = await q.CountAsync(ct);
        var itens = await q.OrderBy(a => a.Codigo).Skip((numero - 1) * tamanho).Take(tamanho).ToListAsync(ct);
        return new Pagina<Artigo>(itens, total, numero, tamanho);
    }

    public Task<Artigo?> ObterAsync(int id, CancellationToken ct = default) =>
        db.Artigos.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);

    /// <summary>Códigos de taxa de IVA que existem (vigentes ou não), para validar e para os menus.</summary>
    public async Task<List<string>> CodigosTaxaAsync(CancellationToken ct = default) =>
        await db.TaxasIva.AsNoTracking().Select(t => t.Codigo).Distinct().OrderBy(c => c).ToListAsync(ct);

    public async Task<(Resultado Resultado, int? Id)> GuardarAsync(int? id, DadosArtigo dados, CancellationToken ct = default)
    {
        var codigo = (dados.Codigo ?? "").Trim().ToUpperInvariant();
        var erros = Validar(dados with { Codigo = codigo });
        var taxa = (dados.CodigoTaxaIva ?? "").Trim().ToUpperInvariant();
        if (!await db.TaxasIva.AnyAsync(t => t.Codigo == taxa, ct)) erros.Add("Escolha uma taxa de IVA que exista em Configurações.");
        if (codigo.Length > 0 && await db.Artigos.AnyAsync(a => a.Codigo == codigo && a.Id != id, ct))
            erros.Add($"Já existe um artigo com o código {codigo}.");
        if (erros.Count > 0) return (Resultado.Falha([.. erros]), id);

        Artigo artigo;
        object? antes = null;
        if (id is null)
        {
            artigo = new Artigo();
            db.Artigos.Add(artigo);
        }
        else
        {
            artigo = await db.Artigos.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new KeyNotFoundException("Artigo não encontrado.");
            antes = Instantaneo(artigo);
        }

        artigo.Codigo = codigo;
        artigo.Descricao = dados.Descricao.Trim();
        artigo.Tipo = dados.Tipo;
        artigo.Unidade = dados.Unidade.Trim();
        artigo.Preco = dados.Preco;
        artigo.CodigoTaxaIva = taxa;

        await db.SaveChangesAsync(ct);
        auditoria.Registar(nameof(Artigo), artigo.Id.ToString(), id is null ? "Artigo.Criar" : "Artigo.Alterar", antes, Instantaneo(artigo));
        await db.SaveChangesAsync(ct);
        return (Resultado.Ok(), artigo.Id);
    }

    /// <summary>Artigos não se apagam (aparecem em documentos emitidos): desactivam-se.</summary>
    public async Task<Resultado> DefinirActivoAsync(int id, bool activo, CancellationToken ct = default)
    {
        var artigo = await db.Artigos.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (artigo is null) return Resultado.Falha("Artigo não encontrado.");
        artigo.Activo = activo;
        auditoria.Registar(nameof(Artigo), id.ToString(), activo ? "Artigo.Activar" : "Artigo.Desactivar");
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public static List<string> Validar(DadosArtigo d)
    {
        var erros = new List<string>();
        if (!CodigoValido().IsMatch(d.Codigo ?? "")) erros.Add("O código só pode ter letras, algarismos, ponto, hífen ou barra (1 a 30 caracteres).");
        if (string.IsNullOrWhiteSpace(d.Descricao)) erros.Add("Indique a descrição.");
        else if (d.Descricao.Trim().Length > 500) erros.Add("A descrição pode ter no máximo 500 caracteres.");
        if (string.IsNullOrWhiteSpace(d.Unidade)) erros.Add("Indique a unidade (ex.: un, h, kg).");
        else if (d.Unidade.Trim().Length > 20) erros.Add("A unidade pode ter no máximo 20 caracteres.");
        if (d.Preco < 0) erros.Add("O preço não pode ser negativo.");
        if (decimal.Round(d.Preco, 2) != d.Preco) erros.Add("O preço só pode ter 2 casas decimais.");
        return erros;
    }

    private static object Instantaneo(Artigo a) =>
        new { a.Codigo, a.Descricao, Tipo = a.Tipo.ToString(), a.Unidade, a.Preco, a.CodigoTaxaIva };

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9.\-/_]{0,29}$")]
    private static partial Regex CodigoValido();
}
