using Bezix.Facturacao.Application.Abstracoes;
using SkiaSharp;

namespace Bezix.Facturacao.Infrastructure.Servicos;

public sealed class ProcessadorImagemSkia : IProcessadorImagem
{
    private static readonly byte[] AssinaturaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] AssinaturaJpeg = [0xFF, 0xD8, 0xFF];

    public ResultadoImagem ProcessarLogotipo(byte[] conteudo, int larguraMaxima) => Processar(conteudo, larguraMaxima, removerFundo: false);

    public ResultadoImagem ProcessarCarimbo(byte[] conteudo, int larguraMaxima) => Processar(conteudo, larguraMaxima, removerFundo: true);

    private static ResultadoImagem Processar(byte[] conteudo, int larguraMaxima, bool removerFundo)
    {
        // Tipo real pelo conteúdo do ficheiro: a extensão e o Content-Type enviados pelo browser não contam.
        var png = conteudo.AsSpan().StartsWith(AssinaturaPng);
        var jpeg = conteudo.AsSpan().StartsWith(AssinaturaJpeg);
        if (!png && !jpeg)
            return ResultadoImagem.Invalida("Formato não suportado. Use um ficheiro PNG ou JPG.");

        using var original = SKBitmap.Decode(conteudo);
        if (original is null || original.Width == 0 || original.Height == 0)
            return ResultadoImagem.Invalida("Não foi possível ler a imagem. O ficheiro pode estar danificado.");

        SKBitmap? redimensionada = null;
        try
        {
            var final = original;
            if (original.Width > larguraMaxima)
            {
                var altura = (int)Math.Round(original.Height * (double)larguraMaxima / original.Width);
                redimensionada = original.Resize(new SKImageInfo(larguraMaxima, Math.Max(1, altura)),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (redimensionada is null)
                    return ResultadoImagem.Invalida("Não foi possível redimensionar a imagem.");
                final = redimensionada;
            }

            using var semFundo = removerFundo ? RemoverFundoClaro(final) : null;
            using var imagem = SKImage.FromBitmap(semFundo ?? final);
            using var dados = imagem.Encode(SKEncodedImageFormat.Png, 100);
            return new ResultadoImagem(true, dados.ToArray(), null);
        }
        finally
        {
            redimensionada?.Dispose();
        }
    }

    /// <summary>
    /// Torna transparente o papel à volta de um carimbo ou assinatura: os pixels quase brancos ficam transparentes
    /// e os claros ficam meio transparentes, para a tinta assentar sobre o documento sem um rectângulo branco.
    /// </summary>
    private static SKBitmap RemoverFundoClaro(SKBitmap origem)
    {
        var destino = new SKBitmap(new SKImageInfo(origem.Width, origem.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var tela = new SKCanvas(destino))
        {
            tela.Clear(SKColors.Transparent);
            tela.DrawBitmap(origem, 0, 0);
        }
        const int Papel = 235, Tinta = 185;
        var pixels = destino.Pixels;
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            var claro = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
            if (claro >= Papel) pixels[i] = SKColors.Transparent;
            else if (claro > Tinta)
                pixels[i] = c.WithAlpha((byte)(c.Alpha * (Papel - claro) / (Papel - Tinta)));
        }
        destino.Pixels = pixels;
        return destino;
    }
}
