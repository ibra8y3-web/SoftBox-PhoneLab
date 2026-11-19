using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record ScreenImageAnalysis(
    string SourceFile, int Width, int Height, int SuspectedOutlierPixels,
    double UniformityDeviationPercent, double MeanLuminance,
    string Interpretation);

/// <summary>
/// Screening analysis for a photograph of the physical display showing a uniform test pattern.
/// A phone screenshot cannot reveal panel defects because it is captured before display output.
/// </summary>
public static class ScreenImageAnalyzer
{
    public static ScreenImageAnalysis AnalyzePhysicalScreenPhoto(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("Screen photo not found.", filePath);
        using var stream = File.OpenRead(filePath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidDataException("Image contains no frames.");
        BitmapSource source = decoder.Frames[0];

        const int maxDimension = 1600;
        var scale = Math.Min(1d, maxDimension / (double)Math.Max(source.PixelWidth, source.PixelHeight));
        if (scale < 1)
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        converted.CopyPixels(pixels, stride, 0);

        static double Luma(byte b, byte g, byte r) => (0.0722 * b) + (0.7152 * g) + (0.2126 * r);
        const int tileRows = 20;
        const int tileColumns = 12;
        var sums = new double[tileRows * tileColumns];
        var counts = new int[sums.Length];
        double total = 0;
        long samples = 0;
        var marginX = Math.Max(2, width / 30);
        var marginY = Math.Max(2, height / 30);

        for (var y = marginY; y < height - marginY; y += 2)
        {
            for (var x = marginX; x < width - marginX; x += 2)
            {
                var offset = y * stride + x * 4;
                var lum = Luma(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
                total += lum; samples++;
                var tx = Math.Min(tileColumns - 1, x * tileColumns / width);
                var ty = Math.Min(tileRows - 1, y * tileRows / height);
                var tile = ty * tileColumns + tx;
                sums[tile] += lum; counts[tile]++;
            }
        }

        var tileMeans = new List<double>();
        for (var i = 0; i < sums.Length; i++)
            if (counts[i] > 0) tileMeans.Add(sums[i] / counts[i]);

        var mean = samples == 0 ? 0 : total / samples;
        var tileMean = tileMeans.Count == 0 ? 0 : tileMeans.Average();
        var deviation = tileMean <= 0 || tileMeans.Count == 0 ? 0 :
            (tileMeans.Max() - tileMeans.Min()) / tileMean * 100d;

        var candidates = 0;
        Span<double> neighbors = stackalloc double[8];
        for (var y = 2; y < height - 2; y += 2)
        {
            for (var x = 2; x < width - 2; x += 2)
            {
                var index = y * stride + x * 4;
                var centre = Luma(pixels[index], pixels[index + 1], pixels[index + 2]);
                var n = 0;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var ni = (y + dy) * stride + (x + dx) * 4;
                        neighbors[n++] = Luma(pixels[ni], pixels[ni + 1], pixels[ni + 2]);
                    }
                neighbors.Sort();
                var median = (neighbors[3] + neighbors[4]) / 2d;
                if (Math.Abs(centre - median) > 75) candidates++;
            }
        }

        var interpretation = "مؤشرات مرشحة فقط. الانعكاس والتموج البصري والتركيز والتعريض قد تنتج إنذارات كاذبة؛ يلزم صورة معايرة واختبار أكثر من لون قبل تثبيت عيب بكسل.";
        if (deviation > 18)
            interpretation += " فرق الإضاءة بين مناطق الصورة مرتفع؛ تحقق من زاوية التصوير والإضاءة الخارجية ثم أعد الاختبار.";
        return new ScreenImageAnalysis(filePath, width, height, candidates, deviation, mean, interpretation);
    }
}
