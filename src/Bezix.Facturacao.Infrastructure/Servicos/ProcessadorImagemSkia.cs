using Bezix.Facturacao.Application.Abstracoes;
using SkiaSharp;

namespace Bezix.Facturacao.Infrastructure.Servicos;

public sealed class ProcessadorImagemSkia : IProcessadorImagem
{
    private static readonly byte[] AssinaturaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] AssinaturaJpeg = [0xFF, 0xD8, 0xFF];

    public ResultadoImagem ProcessarLogotipo(byte[] conteudo, int larguraMaxima)
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

            using var imagem = SKImage.FromBitmap(final);
            using var dados = imagem.Encode(SKEncodedImageFormat.Png, 100);
            return new ResultadoImagem(true, dados.ToArray(), null);
        }
        finally
        {
            redimensionada?.Dispose();
        }
    }
}
