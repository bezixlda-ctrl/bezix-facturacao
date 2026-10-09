using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Automacao;
using Bezix.Facturacao.Infrastructure.Persistencia;
using Bezix.Facturacao.Infrastructure.Servicos;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.Dac;

namespace Bezix.Facturacao.Infrastructure.Backups;

public sealed class OpcoesBackups
{
    /// <summary>"Nenhum" (por omissão), "Pasta" ou "S3".</summary>
    public string Destino { get; set; } = "Nenhum";

    /// <summary>Chave AES-256 em Base64 (32 bytes). Guardar uma cópia fora do servidor: sem ela os backups não se abrem.</summary>
    public string? Chave { get; set; }

    public string Pasta { get; set; } = "";
    public OpcoesS3 S3 { get; set; } = new();

    /// <summary>Pastas incluídas no backup de ficheiros, além da pasta dos uploads (relativas ao ContentRoot).</summary>
    public List<string> PastasExtra { get; set; } = ["App_Data/chaves"];

    /// <summary>Intervalo mínimo entre backups automáticos (o endpoint pode ser chamado mais vezes).</summary>
    public int IntervaloHoras { get; set; } = 20;

    public string NomeBase { get; set; } = "bezix";

    public sealed class OpcoesS3
    {
        public string? Endpoint { get; set; }
        public string? Regiao { get; set; }
        public string Bucket { get; set; } = "";
        public string? Prefixo { get; set; }
        public string ChaveAcesso { get; set; } = "";
        public string ChaveSecreta { get; set; } = "";
    }

    public bool Configurado => !Destino.Equals("Nenhum", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(Chave);

    public byte[] ChaveBytes() => Convert.FromBase64String(Chave ?? throw new InvalidOperationException("Falta Backups:Chave."));
}

public sealed record ResultadoBackup(bool Executado, string Mensagem, IReadOnlyList<string> Enviados, IReadOnlyList<string> Apagados);

public sealed record VerificacaoBackup(string Nome, bool Valido, string Detalhe);

/// <summary>
/// Backup diário fora do Winhost (decisão D23): a base de dados em BACPAC (exportação lógica completa, restaurável com
/// SqlPackage ou SSMS em qualquer SQL Server) e os ficheiros (PDFs, logótipos, comprovativos, exportações AT e chaves de
/// cifra) em ZIP. Os dois são cifrados com AES-256-GCM antes de saírem do servidor; aplica a retenção 7/4/12.
/// </summary>
public sealed class ServicoBackups(
    AppDbContext db,
    IOptions<OpcoesBackups> opcoes,
    IOptions<OpcoesFicheiros> ficheiros,
    IDestinoBackup destino,
    IHostEnvironmentRaiz raiz,
    TimeProvider relogio,
    ILogger<ServicoBackups> log)
{
    public const string Tarefa = "Backup";
    private static readonly SemaphoreSlim EmCurso = new(1, 1);

    private OpcoesBackups O => opcoes.Value;

    public async Task<DateTime?> UltimoSucessoAsync(CancellationToken ct = default) =>
        await db.ExecucoesTarefas.AsNoTracking().Where(e => e.Tarefa == Tarefa && e.Sucesso)
            .OrderByDescending(e => e.Id).Select(e => (DateTime?)e.Fim).FirstOrDefaultAsync(ct);

    /// <summary>Faz o backup se o último tiver mais de <see cref="OpcoesBackups.IntervaloHoras"/> horas (ou se <paramref name="forcar"/>).</summary>
    public async Task<ResultadoBackup> ExecutarAsync(bool forcar = false, CancellationToken ct = default)
    {
        if (!O.Configurado) return new(false, "Backups externos não configurados (Backups:Destino e Backups:Chave).", [], []);
        if (!forcar && await UltimoSucessoAsync(ct) is { } ultimo && relogio.GetUtcNow().UtcDateTime - ultimo < TimeSpan.FromHours(O.IntervaloHoras))
            return new(false, $"Último backup às {ultimo:yyyy-MM-dd HH:mm} UTC; o próximo só depois de {O.IntervaloHoras} h.", [], []);
        if (!await EmCurso.WaitAsync(0, ct)) return new(false, "Já há um backup em curso.", [], []);

        var inicio = relogio.GetUtcNow().UtcDateTime;
        var cronometro = Stopwatch.StartNew();
        var temp = Directory.CreateTempSubdirectory("bezix-backup-");
        try
        {
            var marca = inicio.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var chave = O.ChaveBytes();
            var enviados = new List<string>();

            var bacpac = Path.Combine(temp.FullName, "bd.bacpac");
            await ExportarBaseDadosAsync(bacpac, ct);
            enviados.Add(await CifrarEEnviarAsync(bacpac, $"{O.NomeBase}-{marca}-bd.bacpac.bzx", chave, ct));

            var zip = Path.Combine(temp.FullName, "ficheiros.zip");
            var n = ComprimirFicheiros(zip);
            enviados.Add(await CifrarEEnviarAsync(zip, $"{O.NomeBase}-{marca}-ficheiros.zip.bzx", chave, ct));

            var apagados = await AplicarRetencaoAsync(ct);
            var mensagem = $"{enviados.Count} ficheiros enviados para {destino.Descricao} ({n} ficheiros da aplicação); {apagados.Count} antigos apagados.";
            await RegistarAsync(inicio, true, mensagem, null, ct);
            log.LogInformation("Backup concluído em {Ms} ms: {Mensagem}", cronometro.ElapsedMilliseconds, mensagem);
            return new(true, mensagem, enviados, apagados);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Falha no backup.");
            await RegistarAsync(inicio, false, null, ex.Message, CancellationToken.None);
            throw;
        }
        finally
        {
            EmCurso.Release();
            try { temp.Delete(recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Descarrega, decifra e abre um backup (confirma que a chave e o ficheiro estão bons, sem restaurar).</summary>
    public async Task<VerificacaoBackup> VerificarAsync(string nome, CancellationToken ct = default)
    {
        var temp = Directory.CreateTempSubdirectory("bezix-verificar-");
        try
        {
            var cifrado = Path.Combine(temp.FullName, "c.bzx");
            var claro = Path.Combine(temp.FullName, "claro");
            await destino.DescarregarAsync(nome, cifrado, ct);
            await DecifrarFicheiroAsync(cifrado, claro, O.ChaveBytes(), ct);
            using var z = ZipFile.OpenRead(claro);
            if (nome.Contains("-bd.bacpac"))
            {
                var tabelas = z.Entries.Count(e => e.FullName.StartsWith("Data/", StringComparison.Ordinal) && e.FullName.EndsWith(".BCP", StringComparison.OrdinalIgnoreCase));
                var modelo = z.GetEntry("model.xml") is not null;
                return new(nome, modelo, modelo ? $"BACPAC válido: {tabelas} blocos de dados, {new FileInfo(claro).Length / 1024:N0} KB." : "Falta o model.xml.");
            }
            return new(nome, true, $"ZIP válido: {z.Entries.Count} ficheiros.");
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or InvalidDataException)
        {
            return new(nome, false, "Não abre: " + ex.Message);
        }
        finally
        {
            try { temp.Delete(recursive: true); } catch (IOException) { }
        }
    }

    public Task<List<ItemBackup>> ListarAsync(CancellationToken ct = default) => destino.ListarAsync(ct);

    public static async Task DecifrarFicheiroAsync(string origem, string destinoLocal, byte[] chave, CancellationToken ct = default)
    {
        await using var e = File.OpenRead(origem);
        await using var s = File.Create(destinoLocal);
        await CifraBackup.DecifrarAsync(e, s, chave, ct);
    }

    private async Task ExportarBaseDadosAsync(string caminho, CancellationToken ct)
    {
        var ligacao = db.Database.GetConnectionString() ?? throw new InvalidOperationException("Sem ligação à base de dados.");
        var nome = new SqlConnectionStringBuilder(ligacao).InitialCatalog;
        var dac = new DacServices(ligacao);
        await Task.Run(() => dac.ExportBacpac(caminho, nome, new DacExportOptions { VerifyFullTextDocumentTypesSupported = false }, null, ct), ct);
    }

    private int ComprimirFicheiros(string zip)
    {
        var pastas = new List<(string Origem, string Prefixo)> { (Path.GetFullPath(Path.Combine(raiz.ContentRoot, ficheiros.Value.Raiz)), "uploads") };
        pastas.AddRange(O.PastasExtra.Select(p => (Path.GetFullPath(Path.Combine(raiz.ContentRoot, p)), p.Replace('\\', '/').Split('/')[^1])));
        var n = 0;
        using var arquivo = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (origem, prefixo) in pastas.Where(p => Directory.Exists(p.Origem)))
            foreach (var f in Directory.EnumerateFiles(origem, "*", SearchOption.AllDirectories))
            {
                var relativo = prefixo + "/" + Path.GetRelativePath(origem, f).Replace('\\', '/');
                // Os PDFs e imagens já vêm comprimidos.
                var nivel = Path.GetExtension(f) is ".pdf" or ".png" or ".jpg" or ".zip" ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                arquivo.CreateEntryFromFile(f, relativo, nivel);
                n++;
            }
        return n;
    }

    private async Task<string> CifrarEEnviarAsync(string origem, string nome, byte[] chave, CancellationToken ct)
    {
        var cifrado = origem + ".bzx";
        await using (var e = File.OpenRead(origem))
        await using (var s = File.Create(cifrado))
            await CifraBackup.CifrarAsync(e, s, chave, ct);
        await destino.EnviarAsync(nome, cifrado, ct);
        return nome;
    }

    private async Task<List<string>> AplicarRetencaoAsync(CancellationToken ct)
    {
        var itens = await destino.ListarAsync(ct);
        var prefixo = O.NomeBase + "-";
        var porConjunto = itens.Where(i => i.Nome.StartsWith(prefixo, StringComparison.Ordinal))
            .Select(i => (Item: i, Data: DateTime.TryParseExact(i.Nome.Substring(prefixo.Length, Math.Min(15, Math.Max(0, i.Nome.Length - prefixo.Length))),
                "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : (DateTime?)null))
            .Where(x => x.Data is not null).ToList();
        var manter = RetencaoBackups.AManter(porConjunto.Select(x => x.Data!.Value));
        var apagados = new List<string>();
        foreach (var x in porConjunto.Where(x => !manter.Contains(x.Data!.Value)))
        {
            await destino.ApagarAsync(x.Item.Nome, ct);
            apagados.Add(x.Item.Nome);
        }
        return apagados;
    }

    private async Task RegistarAsync(DateTime inicio, bool sucesso, string? resultado, string? erro, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        db.ExecucoesTarefas.Add(new ExecucaoTarefa
        {
            Tarefa = Tarefa, Inicio = inicio, Fim = relogio.GetUtcNow().UtcDateTime, Sucesso = sucesso,
            Resultado = resultado, Erro = erro is null ? null : erro[..Math.Min(erro.Length, 2000)],
        });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>ContentRoot da aplicação (para resolver App_Data sem depender do ASP.NET na infraestrutura).</summary>
public interface IHostEnvironmentRaiz
{
    string ContentRoot { get; }
}

public sealed class HostEnvironmentRaiz(string contentRoot) : IHostEnvironmentRaiz
{
    public string ContentRoot => contentRoot;
}
