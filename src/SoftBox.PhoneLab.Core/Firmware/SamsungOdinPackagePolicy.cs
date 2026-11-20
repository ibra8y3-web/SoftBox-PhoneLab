using System.IO;

namespace SoftBox.PhoneLab.Core.Firmware;

public enum SamsungOdinSlot
{
    BL,
    AP,
    CP,
    CSC,
    HomeCsc
}

public sealed record SamsungOdinPackageFile(SamsungOdinSlot Slot, string Path);

/// <summary>
/// Validates local Odin slot selection only. It does not establish firmware authenticity,
/// device compatibility, or a successful flash.
/// </summary>
public static class SamsungOdinPackagePolicy
{
    private static readonly SamsungOdinSlot[] SlotOrder =
    [
        SamsungOdinSlot.BL,
        SamsungOdinSlot.AP,
        SamsungOdinSlot.CP,
        SamsungOdinSlot.CSC,
        SamsungOdinSlot.HomeCsc
    ];

    public static IReadOnlyList<SamsungOdinPackageFile> BuildPlan(
        IEnumerable<SamsungOdinPackageFile> selectedPackages)
    {
        ArgumentNullException.ThrowIfNull(selectedPackages);
        var packages = selectedPackages.ToArray();
        if (packages.Length is < 1 or > 5)
            throw new InvalidDataException("حدد ملف Firmware واحدًا على الأقل وبحد أقصى خمسة مواضع.");

        if (packages.Any(item => item is null || !Enum.IsDefined(item.Slot)))
            throw new InvalidDataException("موضع Odin غير صالح.");

        if (!packages.Any(item => item.Slot == SamsungOdinSlot.AP))
            throw new InvalidDataException("ملف AP مطلوب ضمن خطة Odin.");

        if (packages.Any(item => item.Slot == SamsungOdinSlot.CSC) &&
            packages.Any(item => item.Slot == SamsungOdinSlot.HomeCsc))
            throw new InvalidDataException("اختر CSC أو HOME_CSC فقط، ولا تستخدم الموضعين معًا.");

        if (packages.Select(item => item.Slot).Distinct().Count() != packages.Length)
            throw new InvalidDataException("يوجد أكثر من ملف محدد لنفس موضع Odin.");

        var normalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in packages)
        {
            if (string.IsNullOrWhiteSpace(item.Path))
                throw new InvalidDataException("مسار حزمة Odin فارغ.");
            var fullPath = Path.GetFullPath(item.Path);
            if (!IsSupportedPackagePath(fullPath))
                throw new InvalidDataException("حزمة Odin يجب أن تكون ملف TAR أو TAR.MD5: " + Path.GetFileName(fullPath));
            if (!normalizedPaths.Add(fullPath))
                throw new InvalidDataException("تم اختيار الملف نفسه لأكثر من موضع Odin.");
        }

        return packages
            .Select(item => new SamsungOdinPackageFile(item.Slot, Path.GetFullPath(item.Path)))
            .OrderBy(item => Array.IndexOf(SlotOrder, item.Slot))
            .ToArray();
    }

    public static bool IsSupportedPackagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        return path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".tar.md5", StringComparison.OrdinalIgnoreCase);
    }
}
