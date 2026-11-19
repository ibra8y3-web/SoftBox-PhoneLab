using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record ScreenImageAnalysis(
    string SourceFile, string PatternKind, int Width, int Height,
    int SuspectedOutlierPixels, int DarkCandidatePixels, int BrightCandidatePixels, int ColorCandidatePixels,
    double UniformityDeviationPercent, double MeanLuminance, string Interpretation);

/// <summary>Camera-photo screening of a physical panel showing a known uniform test pattern.</summary>
public static class ScreenImageAnalyzer
{
    public static ScreenImageAnalysis AnalyzePhysicalScreenPhoto(string filePath, string patternKind = "unknown")
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("Screen photo not found.", filePath);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "white", "black", "red", "green", "blue", "gray", "unknown" };
        if (!allowed.Contains(patternKind)) patternKind = "unknown";
        using var stream = File.OpenRead(filePath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidDataException("Image contains no frames.");
        BitmapSource source = decoder.Frames[0];
        var originalWidth = source.PixelWidth;
        var originalHeight = source.PixelHeight;
        const int maxDimension = 1600;
        var scale = Math.Min(1d, maxDimension / (double)Math.Max(originalWidth, originalHeight));
        if (scale < 1) source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth; var height = converted.PixelHeight;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        converted.CopyPixels(pixels, stride, 0);
        static double Luma(double b, double g, double r) => 0.0722 * b + 0.7152 * g + 0.2126 * r;
        static double Median(Span<double> values) { values.Sort(); return (values[3] + values[4]) / 2d; }
        const int tileRows = 20, tileColumns = 12;
        var sums = new double[tileRows * tileColumns]; var counts = new int[sums.Length];
        double total = 0; long samples = 0;
        var marginX = Math.Max(2, width / 30); var marginY = Math.Max(2, height / 30);
        for (var y = marginY; y < height - marginY; y += 2)
        for (var x = marginX; x < width - marginX; x += 2)
        {
            var offset = y * stride + x * 4;
            var lum = Luma(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
            total += lum; samples++;
            var tx = Math.Min(tileColumns - 1, x * tileColumns / width);
            var ty = Math.Min(tileRows - 1, y * tileRows / height);
            var tile = ty * tileColumns + tx; sums[tile] += lum; counts[tile]++;
        }
        var tileMeans = new List<double>();
        for (var i = 0; i < sums.Length; i++) if (counts[i] > 0) tileMeans.Add(sums[i] / counts[i]);
        var mean = samples == 0 ? 0 : total / samples;
        var tileMean = tileMeans.Count == 0 ? 0 : tileMeans.Average();
        var deviation = tileMean <= 0 || tileMeans.Count == 0 ? 0 : (tileMeans.Max() - tileMeans.Min()) / tileMean * 100d;
        var candidates = 0; var dark = 0; var bright = 0; var color = 0;
        Span<double> neighborLuma = stackalloc double[8];
        Span<double> neighborR = stackalloc double[8];
        Span<double> neighborG = stackalloc double[8];
        Span<double> neighborB = stackalloc double[8];
        for (var y = Math.Max(2, marginY); y < Math.Min(height - 2, height - marginY); y += 2)
        for (var x = Math.Max(2, marginX); x < Math.Min(width - 2, width - marginX); x += 2)
        {
            var offset = y * stride + x * 4;
            var b = (double)pixels[offset]; var g = (double)pixels[offset + 1]; var r = (double)pixels[offset + 2];
            var centreLuma = Luma(b, g, r); var n = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var ni = (y + dy) * stride + (x + dx) * 4;
                neighborB[n] = pixels[ni]; neighborG[n] = pixels[ni + 1]; neighborR[n] = pixels[ni + 2];
                neighborLuma[n] = Luma(neighborB[n], neighborG[n], neighborR[n]); n++;
            }
            var localLuma = Median(neighborLuma);
            var localR = Median(neighborR); var localG = Median(neighborG); var localB = Median(neighborB);
            var delta = centreLuma - localLuma;
            var channelDeviation = Math.Max(Math.Abs(r - localR), Math.Max(Math.Abs(g - localG), Math.Abs(b - localB)));
            var isDark = delta < -65; var isBright = delta > 65; var isColor = channelDeviation > 75 && Math.Abs(delta) < 85;
            switch (patternKind.ToLowerInvariant())
            {
                case "white": case "gray": isDark = delta < -55; isBright = delta > 70; isColor = channelDeviation > 65 && Math.Abs(delta) < 85; break;
                case "black": isBright = delta > 35; isDark = false; isColor = channelDeviation > 55 && delta > 20; break;
                case "red": isDark = r < localR - 65; isBright = g > localG + 65 || b > localB + 65; isColor = Math.Abs(r-localR)>55 || Math.Abs(g-localG)>55 || Math.Abs(b-localB)>55; break;
                case "green": isDark = g < localG - 65; isBright = r > localR + 65 || b > localB + 65; isColor = Math.Abs(r-localR)>55 || Math.Abs(g-localG)>55 || Math.Abs(b-localB)>55; break;
                case "blue": isDark = b < localB - 65; isBright = r > localR + 65 || g > localG + 65; isColor = Math.Abs(r-localR)>55 || Math.Abs(g-localG)>55 || Math.Abs(b-localB)>55; break;
            }
            if (isDark || isBright || isColor) { candidates++; if (isDark) dark++; if (isBright) bright++; if (isColor) color++; }
        }
        var interpretation = $"النمط {patternKind}: مرشحات داكنة {dark}، مضيئة {bright}، لونية {color}. هذه مؤشرات على صورة كاميرا وليست عدًا مؤكدًا لبكسلات الشاشة." +
            " يجب التصوير من الأمام، وتثبيت التركيز والتعريض، وإزالة الانعكاسات والمواريه قبل اعتماد أي عيب.";
        if (deviation > 18) interpretation += " تفاوت الإضاءة مرتفع؛ أعد التصوير بزاوية وإضاءة أفضل.";
        if (scale < 1) interpretation += $" تم تصغير الصورة من {originalWidth}×{originalHeight} إلى {width}×{height}؛ المواقع والإحصاءات تقريبية.";
        return new ScreenImageAnalysis(filePath, patternKind, width, height, candidates, dark, bright, color, deviation, mean, interpretation);
    }
}