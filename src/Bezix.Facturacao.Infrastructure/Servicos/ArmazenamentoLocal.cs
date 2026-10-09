using Bezix.Facturacao.Application.Abstracoes;
using Microsoft.Extensions.Options;

namespace Bezix.Facturacao.Infrastructure.Servicos;

public sealed class OpcoesFicheiros
{
    /// <summary>Pasta raiz dos uploads e PDFs. Relativa ao ContentRoot ou absoluta. Nunca dentro de wwwroot.</summary>
    public string Raiz { get; set; } = "App_Data/uploads";
}

/// <summary>Guarda ficheiros com nomes aleatórios, fora de wwwroot; só serve o que a aplicação pede explicitamente.</summary>
public sealed class ArmazenamentoLocal : IArmazenamentoFicheiros
{
    private readonly string _raiz;

    public ArmazenamentoLocal(IOptions<OpcoesFicheiros> opcoes, string contentRoot)
    {
        _raiz = Path.GetFullPath(Path.Combine(contentRoot, opcoes.Value.Raiz));
        Directory.CreateDirectory(_raiz);
    }

    public string Raiz => _raiz;

    public async Task<string> GuardarAsync(string pasta, string extensao, byte[] conteudo, CancellationToken ct = default)
    {
        if (pasta.Any(c => !char.IsLetterOrDigit(c) && c != '-')) throw new ArgumentException("Nome de pasta inválido.", nameof(pasta));
        if (extensao is not (".png" or ".jpg" or ".pdf" or ".csv" or ".zip")) throw new ArgumentException("Extensão não permitida.", nameof(extensao));

        var relativo = $"{pasta}/{DateTime.UtcNow:yyyy}/{Guid.NewGuid():N}{extensao}";
        var completo = Resolver(relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(completo)!);
        await File.WriteAllBytesAsync(completo, conteudo, ct);
        return relativo;
    }

    public Stream? Abrir(string caminhoRelativo)
    {
        var completo = Resolver(caminhoRelativo);
        return File.Exists(completo) ? File.OpenRead(completo) : null;
    }

    public void Apagar(string caminhoRelativo)
    {
        var completo = Resolver(caminhoRelativo);
        if (File.Exists(completo)) File.Delete(completo);
    }

    public int ApagarAntigos(string pasta, TimeSpan idade)
    {
        var dir = Resolver(pasta);
        if (!Directory.Exists(dir)) return 0;
        var limite = DateTime.UtcNow - idade;
        var n = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            if (File.GetLastWriteTimeUtc(f) >= limite) continue;
            try { File.Delete(f); n++; } catch (IOException) { }
        }
        return n;
    }

    /// <summary>Impede que um caminho guardado na BD aponte para fora da pasta raiz.</summary>
    private string Resolver(string relativo)
    {
        var completo = Path.GetFullPath(Path.Combine(_raiz, relativo));
        if (!completo.StartsWith(_raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Caminho fora da pasta de ficheiros.");
        return completo;
    }
}
