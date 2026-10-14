using System.Text.RegularExpressions;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Domain.Configuracao;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Bezix.Facturacao.Application.Configuracao;

public sealed record DadosEmpresa(
    string Nome,
    string Nuit,
    string? Slogan,
    string Endereco,
    string? Cidade,
    string? Provincia,
    string? Telefones,
    string? Email,
    string? Site,
    string CorPrimaria,
    string RodapeTexto,
    int PrazoPagamentoDias,
    decimal LimiteNuitObrigatorio,
    ModoArredondamentoIva ModoArredondamentoIva,
    bool RestringirAosProprios = false,
    string? AssinaturaEmail = null,
    string? UrlPublica = null,
    int DiasValidadeLink = 90);

public sealed partial class ServicoEmpresa(
    IAppDbContext db,
    IArmazenamentoFicheiros ficheiros,
    IProcessadorImagem imagens,
    IServicoAuditoria auditoria,
    IMemoryCache cache)
{
    public const int TamanhoMaximoLogotipo = 2 * 1024 * 1024;
    public const int LarguraLogotipo = 600;
    private const string ChaveCache = "empresa";

    /// <summary>Dados da empresa, em cache (lidos em quase todas as páginas e em todos os PDFs).</summary>
    public async Task<Empresa> ObterAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(ChaveCache, out Empresa? emCache) && emCache is not null)
            return emCache;

        var empresa = await db.Empresas.AsNoTracking().SingleOrDefaultAsync(e => e.Id == Empresa.IdUnico, ct)
            ?? throw new InvalidOperationException("O registo da empresa não existe. A base de dados não foi inicializada.");
        cache.Set(ChaveCache, empresa, TimeSpan.FromMinutes(5));
        return empresa;
    }

    public async Task<Resultado> ActualizarDadosAsync(DadosEmpresa dados, CancellationToken ct = default)
    {
        var erros = Validar(dados);
        if (erros.Count > 0) return Resultado.Falha([.. erros]);

        var empresa = await db.Empresas.SingleAsync(e => e.Id == Empresa.IdUnico, ct);
        var antes = Instantaneo(empresa);

        empresa.Nome = dados.Nome.Trim();
        empresa.Nuit = dados.Nuit.Trim();
        empresa.Slogan = Limpar(dados.Slogan);
        empresa.Endereco = dados.Endereco.Trim();
        empresa.Cidade = Limpar(dados.Cidade);
        empresa.Provincia = Limpar(dados.Provincia);
        empresa.Telefones = Limpar(dados.Telefones);
        empresa.Email = Limpar(dados.Email);
        empresa.Site = Limpar(dados.Site);
        empresa.CorPrimaria = dados.CorPrimaria.ToUpperInvariant();
        empresa.RodapeTexto = dados.RodapeTexto.Trim();
        empresa.PrazoPagamentoDias = dados.PrazoPagamentoDias;
        empresa.LimiteNuitObrigatorio = dados.LimiteNuitObrigatorio;
        empresa.ModoArredondamentoIva = dados.ModoArredondamentoIva;
        empresa.RestringirAosProprios = dados.RestringirAosProprios;
        empresa.AssinaturaEmail = Limpar(dados.AssinaturaEmail?.Replace("\r\n", "\n"));
        empresa.UrlPublica = Limpar(dados.UrlPublica)?.TrimEnd('/');
        empresa.DiasValidadeLink = dados.DiasValidadeLink;

        auditoria.Registar(nameof(Empresa), empresa.Id.ToString(), "Configuracao.Alterar", antes, Instantaneo(empresa));
        await db.SaveChangesAsync(ct);
        cache.Remove(ChaveCache);
        return Resultado.Ok();
    }

    public async Task<Resultado> ActualizarLogotipoAsync(byte[] conteudo, CancellationToken ct = default)
    {
        if (conteudo.Length == 0) return Resultado.Falha("Escolha um ficheiro de imagem.");
        if (conteudo.Length > TamanhoMaximoLogotipo) return Resultado.Falha("O logótipo tem mais de 2 MB. Reduza a imagem e tente outra vez.");

        var imagem = imagens.ProcessarLogotipo(conteudo, LarguraLogotipo);
        if (!imagem.Valida || imagem.Png is null) return Resultado.Falha(imagem.Erro ?? "Imagem inválida.");

        var empresa = await db.Empresas.SingleAsync(e => e.Id == Empresa.IdUnico, ct);
        var anterior = empresa.LogoPath;
        empresa.LogoPath = await ficheiros.GuardarAsync("logotipos", ".png", imagem.Png, ct);

        auditoria.Registar(nameof(Empresa), empresa.Id.ToString(), "Configuracao.Logotipo", anterior, empresa.LogoPath);
        await db.SaveChangesAsync(ct);
        cache.Remove(ChaveCache);
        // O ficheiro antigo é mantido: documentos já emitidos podem referenciar o logótipo da época.
        return Resultado.Ok();
    }

    public const int LarguraCarimbo = 500;
    /// <summary>Fotografias de telemóvel: aceita-se mais do que no logótipo, porque a imagem é reduzida para 500 px.</summary>
    public const int TamanhoMaximoCarimbo = 8 * 1024 * 1024;

    public enum ImagemDocumento { Carimbo, Assinatura }

    /// <summary>
    /// Carimbo ou assinatura: o fundo claro fica transparente. Como no logótipo, o ficheiro antigo é mantido,
    /// porque os documentos já emitidos guardam o caminho da imagem da época e a 2.ª via tem de sair igual.
    /// </summary>
    public async Task<Resultado> ActualizarImagemDocumentoAsync(ImagemDocumento tipo, byte[] conteudo, CancellationToken ct = default)
    {
        var nome = tipo == ImagemDocumento.Carimbo ? "O carimbo" : "A assinatura";
        if (conteudo.Length == 0) return Resultado.Falha("Escolha um ficheiro de imagem.");
        if (conteudo.Length > TamanhoMaximoCarimbo) return Resultado.Falha($"{nome} tem mais de 8 MB. Reduza a imagem e tente outra vez.");

        var imagem = imagens.ProcessarCarimbo(conteudo, LarguraCarimbo);
        if (!imagem.Valida || imagem.Png is null) return Resultado.Falha(imagem.Erro ?? "Imagem inválida.");

        var empresa = await db.Empresas.SingleAsync(e => e.Id == Empresa.IdUnico, ct);
        var caminho = await ficheiros.GuardarAsync(tipo == ImagemDocumento.Carimbo ? "carimbos" : "assinaturas", ".png", imagem.Png, ct);
        string? anterior;
        if (tipo == ImagemDocumento.Carimbo) { anterior = empresa.CarimboPath; empresa.CarimboPath = caminho; }
        else { anterior = empresa.AssinaturaPath; empresa.AssinaturaPath = caminho; }

        auditoria.Registar(nameof(Empresa), empresa.Id.ToString(), $"Configuracao.{tipo}", anterior, caminho);
        await db.SaveChangesAsync(ct);
        cache.Remove(ChaveCache);
        return Resultado.Ok();
    }

    public async Task RemoverImagemDocumentoAsync(ImagemDocumento tipo, CancellationToken ct = default)
    {
        var empresa = await db.Empresas.SingleAsync(e => e.Id == Empresa.IdUnico, ct);
        var anterior = tipo == ImagemDocumento.Carimbo ? empresa.CarimboPath : empresa.AssinaturaPath;
        if (anterior is null) return;
        if (tipo == ImagemDocumento.Carimbo) empresa.CarimboPath = null; else empresa.AssinaturaPath = null;
        auditoria.Registar(nameof(Empresa), empresa.Id.ToString(), $"Configuracao.{tipo}", anterior, null);
        await db.SaveChangesAsync(ct);
        cache.Remove(ChaveCache);
    }

    public async Task<Resultado> ActualizarSignatarioAsync(string? nome, string? cargo, CancellationToken ct = default)
    {
        nome = string.IsNullOrWhiteSpace(nome) ? null : nome.Trim();
        cargo = string.IsNullOrWhiteSpace(cargo) ? null : cargo.Trim();
        if (nome?.Length > 120 || cargo?.Length > 120) return Resultado.Falha("O nome e o cargo têm no máximo 120 caracteres.");

        var empresa = await db.Empresas.SingleAsync(e => e.Id == Empresa.IdUnico, ct);
        var antes = new { empresa.SignatarioNome, empresa.SignatarioCargo };
        empresa.SignatarioNome = nome;
        empresa.SignatarioCargo = cargo;
        auditoria.Registar(nameof(Empresa), empresa.Id.ToString(), "Configuracao.Signatario", antes, new { nome, cargo });
        await db.SaveChangesAsync(ct);
        cache.Remove(ChaveCache);
        return Resultado.Ok();
    }

    public async Task RemoverLogotipoAsync(CancellationToken ct = default)
    {
        var empresa = await db.Empresas.SingleAsync(e => e.Id == Empresa.IdUnico, ct);
        if (empresa.LogoPath is null) return;
        auditoria.Registar(nameof(Empresa), empresa.Id.ToString(), "Configuracao.Logotipo", empresa.LogoPath, null);
        empresa.LogoPath = null;
        await db.SaveChangesAsync(ct);
        cache.Remove(ChaveCache);
    }

    public static List<string> Validar(DadosEmpresa d)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Nome)) erros.Add("Indique o nome da empresa.");
        if (!NuitValido(d.Nuit)) erros.Add("O NUIT tem de ter 9 algarismos.");
        if (string.IsNullOrWhiteSpace(d.Endereco)) erros.Add("Indique o endereço.");
        if (!string.IsNullOrWhiteSpace(d.Email) && !d.Email.Contains('@')) erros.Add("O email não é válido.");
        if (!CorHex().IsMatch(d.CorPrimaria ?? "")) erros.Add("A cor tem de estar no formato #RRGGBB.");
        if (string.IsNullOrWhiteSpace(d.RodapeTexto)) erros.Add("O rodapé não pode ficar vazio: tem de incluir \"Processado por computador\".");
        if (d.PrazoPagamentoDias is < 0 or > 365) erros.Add("O prazo de pagamento tem de estar entre 0 e 365 dias.");
        if (d.LimiteNuitObrigatorio < 0) erros.Add("O limite do NUIT não pode ser negativo.");
        if (d.AssinaturaEmail?.Length > 1000) erros.Add("A assinatura pode ter no máximo 1000 caracteres.");
        if (!string.IsNullOrWhiteSpace(d.UrlPublica) && (!Uri.TryCreate(d.UrlPublica.Trim(), UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || d.UrlPublica.Length > 200))
            erros.Add("O endereço público tem de começar por https:// (ex.: https://facturacao.empresa.co.mz).");
        if (d.DiasValidadeLink is < 1 or > 365) erros.Add("A validade dos links tem de estar entre 1 e 365 dias.");
        return erros;
    }

    public static bool NuitValido(string? nuit) => nuit is not null && Nuit().IsMatch(nuit.Trim());

    private static string? Limpar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static object Instantaneo(Empresa e) => new
    {
        e.Nome, e.Nuit, e.Slogan, e.Endereco, e.Cidade, e.Provincia, e.Telefones, e.Email, e.Site,
        e.CorPrimaria, e.RodapeTexto, e.PrazoPagamentoDias, e.LimiteNuitObrigatorio, e.ModoArredondamentoIva,
        e.RestringirAosProprios, e.AssinaturaEmail, e.UrlPublica, e.DiasValidadeLink,
    };

    [GeneratedRegex(@"^\d{9}$")]
    private static partial Regex Nuit();

    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}$")]
    private static partial Regex CorHex();
}
