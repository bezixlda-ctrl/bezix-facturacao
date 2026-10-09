using System.Security.Cryptography;
using Bezix.Facturacao.Infrastructure.Backups;

namespace Bezix.Facturacao.Tests.Backups;

public class CifraBackupTests
{
    private static readonly byte[] Chave = CifraBackup.NovaChave();

    private static async Task<byte[]> CifrarAsync(byte[] dados, byte[]? chave = null)
    {
        using var saida = new MemoryStream();
        await CifraBackup.CifrarAsync(new MemoryStream(dados), saida, chave ?? Chave);
        return saida.ToArray();
    }

    private static async Task<byte[]> DecifrarAsync(byte[] cifrado, byte[]? chave = null)
    {
        using var saida = new MemoryStream();
        await CifraBackup.DecifrarAsync(new MemoryStream(cifrado), saida, chave ?? Chave);
        return saida.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024 * 1024)]          // exactamente um bloco
    [InlineData(1024 * 1024 + 1)]
    [InlineData(2_500_000)]
    public async Task Decifra_exactamente_o_que_foi_cifrado(int tamanho)
    {
        var dados = RandomNumberGenerator.GetBytes(tamanho);
        var cifrado = await CifrarAsync(dados);
        Assert.Equal(dados, await DecifrarAsync(cifrado));
    }

    [Fact]
    public async Task Cada_cifra_e_diferente_mesmo_com_os_mesmos_dados()
    {
        var dados = new byte[1000];
        Assert.NotEqual(await CifrarAsync(dados), await CifrarAsync(dados));
    }

    [Fact]
    public async Task Byte_alterado_e_detectado()
    {
        var cifrado = await CifrarAsync(RandomNumberGenerator.GetBytes(5000));
        cifrado[100] ^= 1;
        await Assert.ThrowsAnyAsync<CryptographicException>(() => DecifrarAsync(cifrado));
    }

    [Fact]
    public async Task Ficheiro_cortado_no_fim_de_um_bloco_e_detectado()
    {
        // 2 blocos: tirar o último bloco inteiro não pode passar por um ficheiro completo.
        var cifrado = await CifrarAsync(RandomNumberGenerator.GetBytes(1024 * 1024 + 10));
        var cortado = cifrado[..(8 + 4 + 4 + 1024 * 1024 + 16)];
        await Assert.ThrowsAnyAsync<CryptographicException>(() => DecifrarAsync(cortado));
    }

    [Fact]
    public async Task Chave_errada_nao_abre()
    {
        var cifrado = await CifrarAsync(RandomNumberGenerator.GetBytes(100));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => DecifrarAsync(cifrado, CifraBackup.NovaChave()));
    }

    [Fact]
    public async Task Ficheiro_que_nao_e_backup_e_recusado() =>
        await Assert.ThrowsAnyAsync<CryptographicException>(() => DecifrarAsync("PK\u0003\u0004 não sou um backup"u8.ToArray()));
}

public class RetencaoBackupsTests
{
    [Fact]
    public void Diario_durante_um_ano_fica_com_7_diarios_4_semanais_e_12_mensais()
    {
        var hoje = new DateTime(2026, 10, 9, 2, 0, 0, DateTimeKind.Utc);
        var conjuntos = Enumerable.Range(0, 400).Select(i => hoje.AddDays(-i)).ToList();

        var manter = RetencaoBackups.AManter(conjuntos);

        // Os 7 dias mais recentes.
        Assert.All(Enumerable.Range(0, 7), i => Assert.Contains(hoje.AddDays(-i), manter));
        Assert.DoesNotContain(hoje.AddDays(-8), manter);
        // Um por mês nos últimos 12 meses (o mais recente de cada mês).
        Assert.Contains(new DateTime(2026, 9, 30, 2, 0, 0, DateTimeKind.Utc), manter);
        Assert.Contains(new DateTime(2025, 11, 30, 2, 0, 0, DateTimeKind.Utc), manter);
        Assert.DoesNotContain(new DateTime(2025, 10, 31, 2, 0, 0, DateTimeKind.Utc), manter);
        Assert.InRange(manter.Count, 12, 7 + 4 + 12);
    }

    [Fact]
    public void Varios_backups_no_mesmo_dia_mantem_so_o_ultimo()
    {
        var d = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
        var manter = RetencaoBackups.AManter([d.AddHours(1), d.AddHours(9), d.AddHours(23)]);
        Assert.Equal([d.AddHours(23)], manter);
    }
}
