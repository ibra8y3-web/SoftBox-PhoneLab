using System.Buffers.Binary;
using System.IO;
using System.Text;
namespace SoftBox.PhoneLab.Desktop.Services;
public sealed record BoardViewFormatDetection(string FormatId, string DisplayName, bool ContentRecognized, string DetectionDetail);
public static class BoardViewNativeFormatDetector
{
    private const int HeaderBytes = 64 * 1024;
    public static async Task<BoardViewFormatDetection> DetectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Board file path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("BoardView file not found.", fullPath);
        if (info.Length <= 0) throw new InvalidDataException("BoardView file is empty.");
        var bytes = new byte[(int)Math.Min(HeaderBytes, info.Length)];
        await using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var read = 0;
            while (read < bytes.Length)
            {
                var count = await stream.ReadAsync(bytes.AsMemory(read), cancellationToken);
                if (count == 0) break;
                read += count;
            }
            if (read != bytes.Length) Array.Resize(ref bytes, read);
        }
        var content = Encoding.Latin1.GetString(bytes);
        var extension = info.Extension.ToLowerInvariant();
        if (bytes.Length >= 8 && BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, 4)) == 0x23E26328)
            return Known("landrex-brd", "Landrex / TestLink BRD", "تم التعرف على ترويسة BRD الثنائية.");
        if ((extension is ".brd" or ".brd2") && Starts(content, "BRDOUT:") && Has(content, "NETS:"))
            return Known("toptest-brd2", "TOPTEST BRD / BRD2", "ترويسة BRDOUT وقسم NETS موجودان.");
        if (extension == ".bdv" && Has(content, "<<format.asc>>") && (Has(content, "<<pins.asc>>") || Has(content, "<<nails.asc>>")))
            return Known("honhan-bdv", "Honhan BDV", "تم العثور على أقسام format/pins الخاصة بملف BDV.");
        if (extension == ".bvr" && (Has(content, "BVRAW_FORMAT_3") || Has(content, "BVRAW_FORMAT_1")))
            return Known("bvr-bvraw", "BVR / BVRAW", "تم العثور على معرف BVRAW داخل رأس الملف.");
        if ((extension is ".cad" or ".gcd") && Has(content, "GENCAD") && (Has(content, "COMPONENTS") || Has(content, "SIGNALS")))
            return Known("gencad", "GenCAD", "تم التعرف على ترويسة GenCAD وأقسام المكونات/الشبكات.");
        if (extension == ".kicad_pcb" && Has(content, "(kicad_pcb"))
            return Known("kicad-pcb", "KiCad PCB", "تم التعرف على بنية ملف KiCad النصية.");
        if (extension == ".pcbdoc" && bytes.AsSpan().StartsWith(new byte[] { 0xD0,0xCF,0x11,0xE0,0xA1,0xB1,0x1A,0xE1 }))
            return Known("altium-pcbdoc", "Altium PcbDoc", "تم التعرف على حاوية Compound File الثنائية.");
        if (extension == ".brd" && Has(content, "<eagle") && Has(content, "<board"))
            return Known("eagle-board", "EAGLE XML Board", "تم التعرف على عناصر eagle/board في ملف XML.");
        if (extension == ".asc" && Has(content, "eM-Test Expert"))
            return Known("em-test-asc", "eM-Test ASC", "تم العثور على معرف eM-Test في ملف ASC.");
        return new("unidentified", $"صيغة غير محددة ({extension})", false, "لم يطابق أول 64 KiB توقيعًا معروفًا. سيُمرر الملف إلى TRACE ليتحقق قارئ الصيغة نفسه؛ الامتداد وحده ليس دليل دعم.");
    }
    private static BoardViewFormatDetection Known(string id, string name, string detail) => new(id, name, true, detail);
    private static bool Starts(string value, string prefix) => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    private static bool Has(string value, string marker) => value.Contains(marker, StringComparison.OrdinalIgnoreCase);
}
