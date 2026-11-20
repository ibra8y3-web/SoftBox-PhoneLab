using System.IO;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Backup;

/// <summary>
/// Cross-boundary path policy for restoring archive entries into a local staging directory
/// before they are pushed into a newly-created Android shared-storage folder.
/// </summary>
public static class BackupRestorePathPolicy
{
    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.Contains('\\') ||
            path.Contains(':') ||
            path.Any(char.IsControl) ||
            path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("مسار داخل النسخة غير صالح للاستعادة.");

        foreach (var segment in path.Split('/'))
        {
            var deviceName = segment.Split('.')[0];
            if (segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                segment.Length > 180 ||
                deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(deviceName, @"^(COM|LPT)[1-9]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("اسم ملف غير مدعوم على Windows: " + segment);
        }
    }

    public static string ResolveInsideDirectory(string root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(
            fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("مسار الاستخراج يخرج عن مجلد العمل المؤقت.");
        return fullPath;
    }
}
