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

        using var lida = SKBitmap.Decode(conteudo);
        if (lida is null || lida.Width == 0 || lida.Height == 0)
            return ResultadoImagem.Invalida("Não foi possível ler a imagem. O ficheiro pode estar danificado.");
        // Fotografias de telemóvel: aplicar a orientação gravada pela câmara (EXIF), senão ficam deitadas.
        using var codec = SKCodec.Create(new MemoryStream(conteudo));
        using var rodada = Endireitar(lida, codec?.EncodedOrigin ?? SKEncodedOrigin.TopLeft);
        var original = rodada ?? lida;
        if (original.Width == 0 || original.Height == 0)
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

    /// <summary>Roda a imagem conforme a orientação EXIF (só os casos que as câmaras usam: 90°, 180° e 270°).</summary>
    private static SKBitmap? Endireitar(SKBitmap origem, SKEncodedOrigin orientacao)
    {
        var graus = orientacao switch
        {
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.RightTop => 90,
            SKEncodedOrigin.LeftBottom => 270,
            _ => 0,
        };
        if (graus == 0) return null;
        var deitada = graus != 180;
        var destino = new SKBitmap(deitada ? origem.Height : origem.Width, deitada ? origem.Width : origem.Height, origem.ColorType, origem.AlphaType);
        using var tela = new SKCanvas(destino);
        tela.Translate(destino.Width / 2f, destino.Height / 2f);
        tela.RotateDegrees(graus);
        tela.Translate(-origem.Width / 2f, -origem.Height / 2f);
        using var imagem = SKImage.FromBitmap(origem);
        tela.DrawImage(imagem, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), null);
        return destino;
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
            using var imagem = SKImage.FromBitmap(origem);
            tela.DrawImage(imagem, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), null);
        }
        var pixels = destino.Pixels;
        // A cor do papel mede-se nas margens (uma fotografia com pouca luz dá papel cinzento, não branco):
        // tudo o que estiver perto dessa cor fica transparente; a tinta, bem mais escura, mantém-se.
        var margem = new List<int>();
        int w = destino.Width, h = destino.Height, passo = Math.Max(1, (w + h) / 400);
        for (var x = 0; x < w; x += passo) { margem.Add(Claridade(pixels[x])); margem.Add(Claridade(pixels[(h - 1) * w + x])); }
        for (var y = 0; y < h; y += passo) { margem.Add(Claridade(pixels[y * w])); margem.Add(Claridade(pixels[y * w + w - 1])); }
        margem.Sort();
        // Mediana das margens = cor do papel (mesmo que a tinta toque nalgumas margens); a diferença para o
        // quartil inferior mede o grão da fotografia, que também deve desaparecer (limitada, para não comer a tinta).
        var mediana = margem[margem.Count / 2];
        var grao = Math.Min(30, (mediana - margem[margem.Count / 4]) * 3 / 2);
        var Papel = Math.Clamp(mediana - grao - 4, 120, 245);
        var Tinta = Papel - 50;
        for (var i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            var claro = Claridade(c);
            if (claro >= Papel) pixels[i] = SKColors.Transparent;
            else if (claro > Tinta)
                pixels[i] = c.WithAlpha((byte)(c.Alpha * (Papel - claro) / (Papel - Tinta)));
        }
        destino.Pixels = pixels;
        return destino;
    }

    private static int Claridade(SKColor c) => Math.Min(c.Red, Math.Min(c.Green, c.Blue));
}
