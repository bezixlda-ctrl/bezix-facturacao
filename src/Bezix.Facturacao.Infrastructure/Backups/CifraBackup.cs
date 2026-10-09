using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Bezix.Facturacao.Infrastructure.Backups;

/// <summary>
/// Cifra os backups antes de saírem do servidor: AES-256-GCM em blocos de 1 MB (ficheiros grandes sem os ter todos em memória).
/// Formato: "BZXBKP01" | prefixo do nonce (4 bytes) | blocos [tamanho (4 bytes, bit alto = último bloco) | cifrado | tag (16)].
/// O nonce de cada bloco é o prefixo + o n.º do bloco, e o indicador "último bloco" entra como dado autenticado:
/// trocar, repetir ou cortar blocos é detectado na decifra.
/// </summary>
public static class CifraBackup
{
    private static readonly byte[] Assinatura = "BZXBKP01"u8.ToArray();
    private const int TamanhoBloco = 1024 * 1024;
    private const int TamanhoTag = 16;
    private const uint UltimoBloco = 0x8000_0000;

    public static byte[] NovaChave() => RandomNumberGenerator.GetBytes(32);

    public static async Task CifrarAsync(Stream entrada, Stream saida, byte[] chave, CancellationToken ct = default)
    {
        Validar(chave);
        using var aes = new AesGcm(chave, TamanhoTag);
        var prefixo = RandomNumberGenerator.GetBytes(4);
        await saida.WriteAsync(Assinatura, ct);
        await saida.WriteAsync(prefixo, ct);

        var atual = new byte[TamanhoBloco];
        var seguinte = new byte[TamanhoBloco];
        var cifrado = new byte[TamanhoBloco];
        var tag = new byte[TamanhoTag];
        var cabecalho = new byte[4];
        var lidos = await LerTudoAsync(entrada, atual, ct);
        ulong n = 0;
        while (true)
        {
            // Lê o bloco seguinte antes de escrever este, para saber se este é o último.
            var lidosSeguinte = lidos == TamanhoBloco ? await LerTudoAsync(entrada, seguinte, ct) : 0;
            var ultimo = lidosSeguinte == 0;
            aes.Encrypt(Nonce(prefixo, n), atual.AsSpan(0, lidos), cifrado.AsSpan(0, lidos), tag, [ultimo ? (byte)1 : (byte)0]);
            BinaryPrimitives.WriteUInt32BigEndian(cabecalho, (uint)lidos | (ultimo ? UltimoBloco : 0));
            await saida.WriteAsync(cabecalho, ct);
            await saida.WriteAsync(cifrado.AsMemory(0, lidos), ct);
            await saida.WriteAsync(tag, ct);
            if (ultimo) break;
            (atual, seguinte) = (seguinte, atual);
            lidos = lidosSeguinte;
            n++;
        }
    }

    /// <summary>Decifra e autentica. Lança <see cref="CryptographicException"/> se o ficheiro foi alterado, cortado ou a chave está errada.</summary>
    public static async Task DecifrarAsync(Stream entrada, Stream saida, byte[] chave, CancellationToken ct = default)
    {
        Validar(chave);
        using var aes = new AesGcm(chave, TamanhoTag);
        var assinatura = new byte[Assinatura.Length];
        if (await LerTudoAsync(entrada, assinatura, ct) != assinatura.Length || !assinatura.AsSpan().SequenceEqual(Assinatura))
            throw new CryptographicException("Não é um backup cifrado da Bezix Facturação.");
        var prefixo = new byte[4];
        if (await LerTudoAsync(entrada, prefixo, ct) != 4) throw new CryptographicException("Backup incompleto.");

        var cifrado = new byte[TamanhoBloco];
        var claro = new byte[TamanhoBloco];
        var tag = new byte[TamanhoTag];
        var cabecalho = new byte[4];
        for (ulong n = 0; ; n++)
        {
            if (await LerTudoAsync(entrada, cabecalho, ct) != 4) throw new CryptographicException("Backup incompleto (falta o último bloco).");
            var valor = BinaryPrimitives.ReadUInt32BigEndian(cabecalho);
            var ultimo = (valor & UltimoBloco) != 0;
            var tamanho = (int)(valor & ~UltimoBloco);
            if (tamanho > TamanhoBloco) throw new CryptographicException("Bloco inválido.");
            if (await LerTudoAsync(entrada, cifrado.AsMemory(0, tamanho), ct) != tamanho || await LerTudoAsync(entrada, tag, ct) != TamanhoTag)
                throw new CryptographicException("Backup incompleto.");
            aes.Decrypt(Nonce(prefixo, n), cifrado.AsSpan(0, tamanho), tag, claro.AsSpan(0, tamanho), [ultimo ? (byte)1 : (byte)0]);
            await saida.WriteAsync(claro.AsMemory(0, tamanho), ct);
            if (ultimo)
            {
                if (await LerTudoAsync(entrada, new byte[1], ct) != 0) throw new CryptographicException("Dados a mais depois do último bloco.");
                return;
            }
        }
    }

    private static byte[] Nonce(byte[] prefixo, ulong n)
    {
        var nonce = new byte[12];
        prefixo.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), n);
        return nonce;
    }

    private static void Validar(byte[] chave)
    {
        if (chave.Length != 32) throw new ArgumentException("A chave dos backups tem de ter 32 bytes (256 bits).", nameof(chave));
    }

    private static Task<int> LerTudoAsync(Stream s, byte[] buffer, CancellationToken ct) => LerTudoAsync(s, buffer.AsMemory(), ct);

    private static async Task<int> LerTudoAsync(Stream s, Memory<byte> buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await s.ReadAsync(buffer[total..], ct);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
