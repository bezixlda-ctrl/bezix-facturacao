using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Bezix.Facturacao.Infrastructure.Backups;

public sealed record ItemBackup(string Nome, DateTime ModificadoEmUtc, long Tamanho);

/// <summary>Onde ficam os backups cifrados (fora do Winhost).</summary>
public interface IDestinoBackup
{
    string Descricao { get; }
    Task EnviarAsync(string nome, string ficheiroLocal, CancellationToken ct = default);
    Task<List<ItemBackup>> ListarAsync(CancellationToken ct = default);
    Task DescarregarAsync(string nome, string ficheiroLocal, CancellationToken ct = default);
    Task ApagarAsync(string nome, CancellationToken ct = default);
}

/// <summary>Pasta (outro disco, partilha de rede). Usada também nos testes.</summary>
public sealed class DestinoPasta(string pasta) : IDestinoBackup
{
    public string Descricao => $"pasta {pasta}";

    public Task EnviarAsync(string nome, string ficheiroLocal, CancellationToken ct = default)
    {
        Directory.CreateDirectory(pasta);
        File.Copy(ficheiroLocal, Caminho(nome), overwrite: true);
        return Task.CompletedTask;
    }

    public Task<List<ItemBackup>> ListarAsync(CancellationToken ct = default) =>
        Task.FromResult(Directory.Exists(pasta)
            ? new DirectoryInfo(pasta).GetFiles("*.bzx").Select(f => new ItemBackup(f.Name, f.LastWriteTimeUtc, f.Length)).ToList()
            : []);

    public Task DescarregarAsync(string nome, string ficheiroLocal, CancellationToken ct = default)
    {
        File.Copy(Caminho(nome), ficheiroLocal, overwrite: true);
        return Task.CompletedTask;
    }

    public Task ApagarAsync(string nome, CancellationToken ct = default)
    {
        File.Delete(Caminho(nome));
        return Task.CompletedTask;
    }

    private string Caminho(string nome)
    {
        if (nome.Contains('/') || nome.Contains('\\') || nome.Contains("..")) throw new ArgumentException("Nome de backup inválido.", nameof(nome));
        return Path.Combine(pasta, nome);
    }
}

/// <summary>Armazenamento compatível com S3: Amazon S3, Backblaze B2, Wasabi, Cloudflare R2, etc.</summary>
public sealed class DestinoS3(OpcoesBackups.OpcoesS3 o) : IDestinoBackup, IDisposable
{
    private readonly AmazonS3Client _cliente = new(new BasicAWSCredentials(o.ChaveAcesso, o.ChaveSecreta), new AmazonS3Config
    {
        ServiceURL = string.IsNullOrWhiteSpace(o.Endpoint) ? null : o.Endpoint,
        AuthenticationRegion = o.Regiao,
        ForcePathStyle = !string.IsNullOrWhiteSpace(o.Endpoint),
    });

    private string Prefixo => string.IsNullOrWhiteSpace(o.Prefixo) ? "" : o.Prefixo.TrimEnd('/') + "/";

    public string Descricao => $"S3 {o.Bucket}/{Prefixo}";

    public async Task EnviarAsync(string nome, string ficheiroLocal, CancellationToken ct = default) =>
        await _cliente.PutObjectAsync(new PutObjectRequest { BucketName = o.Bucket, Key = Prefixo + nome, FilePath = ficheiroLocal }, ct);

    public async Task<List<ItemBackup>> ListarAsync(CancellationToken ct = default)
    {
        var itens = new List<ItemBackup>();
        var pedido = new ListObjectsV2Request { BucketName = o.Bucket, Prefix = Prefixo };
        ListObjectsV2Response r;
        do
        {
            r = await _cliente.ListObjectsV2Async(pedido, ct);
            foreach (var x in r.S3Objects ?? [])
                if (x.Key.EndsWith(".bzx", StringComparison.Ordinal))
                    itens.Add(new ItemBackup(x.Key[Prefixo.Length..], (x.LastModified ?? DateTime.UtcNow).ToUniversalTime(), x.Size ?? 0));
            pedido.ContinuationToken = r.NextContinuationToken;
        } while (r.IsTruncated == true);
        return itens;
    }

    public async Task DescarregarAsync(string nome, string ficheiroLocal, CancellationToken ct = default)
    {
        using var r = await _cliente.GetObjectAsync(o.Bucket, Prefixo + nome, ct);
        await using var f = File.Create(ficheiroLocal);
        await r.ResponseStream.CopyToAsync(f, ct);
    }

    public async Task ApagarAsync(string nome, CancellationToken ct = default) =>
        await _cliente.DeleteObjectAsync(o.Bucket, Prefixo + nome, ct);

    public void Dispose() => _cliente.Dispose();
}
