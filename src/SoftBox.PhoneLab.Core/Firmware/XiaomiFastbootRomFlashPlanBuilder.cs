using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SoftBox.PhoneLab.Core.Firmware;

public enum XiaomiFastbootRomStepKind
{
    FlashPartition,
    WipeUserData,
    RebootFastbootd
}

public sealed record XiaomiFastbootRomFlashStep(
    int Index,
    XiaomiFastbootRomStepKind Kind,
    string? Partition,
    string? ImagePath,
    long ImageBytes,
    string? Sha256);

public sealed record XiaomiFastbootRomFlashPlan(
    string ScriptPath,
    string PackageRoot,
    string ScriptName,
    IReadOnlyList<string> RequiredProducts,
    string? SelectedDeviceProduct,
    XiaomiRomProductMatchState ProductMatch,
    int? FirmwareAntiRollbackIndex,
    IReadOnlyList<XiaomiFastbootRomFlashStep> Steps,
    IReadOnlyList<string> Findings)
{
    public int FlashImageCount => Steps.Count(step => step.Kind == XiaomiFastbootRomStepKind.FlashPartition);
    public bool RequiresDataWipe => Steps.Any(step => step.Kind == XiaomiFastbootRomStepKind.WipeUserData);
    public long TotalImageBytes => Steps.Where(step => step.Kind == XiaomiFastbootRomStepKind.FlashPartition)
        .Sum(step => step.ImageBytes);

    public bool CanExecute => ProductMatch == XiaomiRomProductMatchState.Match &&
        FlashImageCount > 0 && Steps.Count > 0;
}

/// <summary>
/// Builds a guarded Fastboot operation plan from an already-extracted Xiaomi Fastboot ROM.
/// The Windows batch file is parsed as data and is never executed. Only direct, serial-pinned
/// flash commands, an explicitly acknowledged fastboot -w, and fastboot reboot fastboot are
/// accepted. Unlock, relock, erase, format, arbitrary commands, shell chaining and path escapes
/// are rejected. This is not a cryptographic proof of OEM provenance.
/// </summary>
public static class XiaomiFastbootRomFlashPlanBuilder
{
    private const int MaximumScriptBytes = 8 * 1024 * 1024;
    private const int MaximumAndroidInfoBytes = 8 * 1024 * 1024;
    private const int MaximumFlashSteps = 128;
    private const long MaximumImageBytes = 8L * 1024 * 1024 * 1024;
    private const long MaximumTotalImageBytes = 24L * 1024 * 1024 * 1024;

    private static readonly Regex ProductPattern = new(
        @"(?im)^\s*require\s+product\s*=\s*([A-Za-z0-9._,-]+)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FastbootPrefix = new(
        @"^\s*@?\s*""?fastboot(?:\.exe)?""?\s+(?<arguments>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FastbootMarker = new(
        @"\bfastboot(?:\.exe)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PartitionPattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<XiaomiFastbootRomFlashPlan> BuildAsync(
        string scriptPath,
        string? selectedDeviceProduct,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            throw new FileNotFoundException("ملف سكربت Xiaomi Fastboot ROM غير موجود.", scriptPath);

        var fullScriptPath = Path.GetFullPath(scriptPath);
        var scriptName = Path.GetFileName(fullScriptPath);
        if (!scriptName.Equals("flash_all.bat", StringComparison.OrdinalIgnoreCase) &&
            !scriptName.Equals("flash_all_except_storage.bat", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "يُسمح فقط بـ flash_all.bat أو flash_all_except_storage.bat. سكربت flash_all_lock غير مدعوم.");
        }

        var root = Path.GetDirectoryName(fullScriptPath)
            ?? throw new InvalidDataException("تعذر تحديد مجلد حزمة ROM.");
        var rootInfo = new DirectoryInfo(root);
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("مجلد ROM لا يمكن أن يكون رابطًا رمزيًا أو Junction.");

        var scriptInfo = new FileInfo(fullScriptPath);
        if (scriptInfo.Length is <= 0 or > MaximumScriptBytes ||
            (scriptInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("حجم سكربت ROM غير صالح أو يتجاوز 8 MiB أو أنه رابط رمزي.");

        var scriptBytes = await File.ReadAllBytesAsync(fullScriptPath, cancellationToken);
        string scriptText;
        try
        {
            scriptText = new UTF8Encoding(false, true).GetString(scriptBytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("ترميز سكربت ROM غير مدعوم؛ لم يتم تفسيره.", ex);
        }

        var infoPath = FindAndroidInfo(root);
        if (infoPath is null)
            throw new InvalidDataException("لم يتم العثور على android-info.txt في مجلد ROM أو images.");

        var info = new FileInfo(infoPath);
        if (info.Length is <= 0 or > MaximumAndroidInfoBytes ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("ملف android-info.txt غير صالح أو يتجاوز الحد الآمن.");

        var infoText = await File.ReadAllTextAsync(infoPath, new UTF8Encoding(false, false), cancellationToken);
        var products = ProductPattern.Matches(infoText)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (products.Length == 0)
            throw new InvalidDataException("android-info.txt لا يحتوي على require product واضح؛ لا يمكن إثبات توافق Product.");

        var deviceProduct = string.IsNullOrWhiteSpace(selectedDeviceProduct) ? null : selectedDeviceProduct.Trim();
        var productMatch = deviceProduct is null
            ? XiaomiRomProductMatchState.Unknown
            : products.Contains(deviceProduct, StringComparer.OrdinalIgnoreCase)
                ? XiaomiRomProductMatchState.Match
                : XiaomiRomProductMatchState.Mismatch;

        var steps = new List<XiaomiFastbootRomFlashStep>();
        var findings = new List<string>();
        long totalBytes = 0;
        var wipeCount = 0;
        var logicalIndex = 0;

        foreach (var line in JoinBatchContinuations(scriptText))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("rem", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("::", StringComparison.Ordinal) ||
                trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;

            var prefix = FastbootPrefix.Match(trimmed);
            if (!prefix.Success)
            {
                if (FastbootMarker.IsMatch(trimmed))
                    throw new InvalidDataException("تم العثور على استدعاء Fastboot داخل شرط أو أمر مركب غير مدعوم: " + trimmed);
                continue;
            }

            var rawArguments = prefix.Groups["arguments"].Value.Trim();
            var tokens = Tokenize(rawArguments);
            tokens = ReplaceSerialPlaceholders(tokens);

            if (tokens.Count == 0)
                throw new InvalidDataException("أمر Fastboot فارغ داخل سكربت ROM.");

            var verb = tokens[0].ToLowerInvariant();
            var hasShellOperators = rawArguments.IndexOfAny(['&', '|', '>', '<', ';']) >= 0;

            if (verb == "getvar")
            {
                if (tokens.Count < 2 || hasShellOperators &&
                    !tokens.Skip(1).Any(value => value.Equals("product", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("أمر getvar داخل سكربت ROM غير مفهوم.");
                }
                findings.Add("تم تجاهل استعلام Fastboot للقراءة فقط من السكربت؛ سيقرأ SoftBox هوية الجهاز مباشرة.");
                continue;
            }

            if (hasShellOperators)
                throw new InvalidDataException("أوامر Fastboot ذات pipe أو redirection أو chaining غير مدعومة.");

            if (verb is "erase" or "format" or "flashing" or "oem" or "set_active" or "boot" or "update" or "continue")
                throw new InvalidDataException("السكربت يحتوي أمر Fastboot غير مسموح: " + verb);

            if (verb == "-w")
            {
                if (tokens.Count != 1)
                    throw new InvalidDataException("صيغة fastboot -w غير صالحة.");
                if (!scriptName.Equals("flash_all.bat", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("تم رفض wipe داخل flash_all_except_storage.bat لحماية بيانات المستخدم.");
                if (++wipeCount > 1)
                    throw new InvalidDataException("السكربت يحاول تنفيذ أكثر من عملية wipe؛ تم رفضه.");
                steps.Add(new XiaomiFastbootRomFlashStep(
                    ++logicalIndex, XiaomiFastbootRomStepKind.WipeUserData, null, null, 0, null));
                continue;
            }

            if (verb == "flash")
            {
                if (tokens.Count != 3)
                    throw new InvalidDataException("أمر flash يجب أن يحتوي على قسم واحد ومسار صورة واحد فقط.");
                var partition = tokens[1];
                if (!PartitionPattern.IsMatch(partition))
                    throw new InvalidDataException("اسم قسم Fastboot غير صالح: " + partition);

                var imagePath = ResolveImagePath(root, tokens[2]);
                var imageInfo = new FileInfo(imagePath);
                if (imageInfo.Length is <= 0 or > MaximumImageBytes)
                    throw new InvalidDataException("صورة ROM فارغة أو تتجاوز 8 GiB: " + imageInfo.Name);
                if ((imageInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("صورة ROM لا يمكن أن تكون رابطًا رمزيًا: " + imageInfo.Name);

                totalBytes += imageInfo.Length;
                if (totalBytes > MaximumTotalImageBytes)
                    throw new InvalidDataException("إجمالي صور ROM يتجاوز حد SoftBox البالغ 24 GiB.");

                string hash;
                await using (var input = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken))
                        .ToLowerInvariant();
                }

                steps.Add(new XiaomiFastbootRomFlashStep(
                    ++logicalIndex, XiaomiFastbootRomStepKind.FlashPartition,
                    partition, imagePath, imageInfo.Length, hash));
                if (steps.Count > MaximumFlashSteps + 1)
                    throw new InvalidDataException("عدد أوامر ROM يتجاوز الحد 128 صورة وخطوة wipe واحدة.");
                continue;
            }

            if (verb == "reboot")
            {
                if (tokens.Count == 1)
                {
                    findings.Add("تم استبدال إعادة التشغيل النهائية للسكربت بإعادة تشغيل يتحكم بها SoftBox بعد نجاح جميع الخطوات.");
                    continue;
                }

                if (tokens.Count == 2 && tokens[1].Equals("fastboot", StringComparison.OrdinalIgnoreCase))
                {
                    steps.Add(new XiaomiFastbootRomFlashStep(
                        ++logicalIndex, XiaomiFastbootRomStepKind.RebootFastbootd, null, null, 0, null));
                    continue;
                }

                throw new InvalidDataException("صيغة reboot داخل سكربت ROM غير مدعومة.");
            }

            throw new InvalidDataException("أمر Fastboot غير مدعوم ولن يتم تشغيله: " + verb);
        }

        if (steps.Count(step => step.Kind == XiaomiFastbootRomStepKind.FlashPartition) is < 1 or > MaximumFlashSteps)
            throw new InvalidDataException("لم يتم العثور على أوامر flash صالحة، أو تجاوز عدد الصور الحد 128.");

        if (scriptName.Equals("flash_all_except_storage.bat", StringComparison.OrdinalIgnoreCase) && wipeCount != 0)
            throw new InvalidDataException("سكربت حفظ البيانات يحتوي wipe غير متوقع؛ تم رفضه.");

        int? firmwareAntiRollback = await ReadAntiRollbackIndexAsync(root, cancellationToken);

        findings.Add("لم يتم تشغيل أي ملف BAT أو EXE من حزمة ROM؛ تم تحويل أوامر Fastboot المسموح بها إلى خطوات منفصلة.");
        findings.Add("SoftBox لا ينفذ erase أو format أو unlock أو relock أو أوامر OEM أو أوامر shell.");
        findings.Add("يظل تأكيد المنطقة وإصدار Anti-Rollback ومصدر OEM والنسخة الاحتياطية مسؤولية الفني عند عدم توفر دليل آلي.");
        findings.Add("لا يتم اعتبار SHA-256 دليلًا على ناشر الحزمة أو توقيعها.");

        if (productMatch == XiaomiRomProductMatchState.Match)
            findings.Add("Product من android-info.txt يطابق Product المقروء من Fastboot نصيًا.");
        else if (productMatch == XiaomiRomProductMatchState.Mismatch)
            findings.Add("Product الخاص بالجهاز لا يطابق متطلبات الحزمة؛ التفليش ممنوع.");
        else
            findings.Add("لم يتوفر Product حالي مؤكد من Fastboot؛ التفليش ممنوع.");

        return new XiaomiFastbootRomFlashPlan(
            fullScriptPath,
            root,
            scriptName,
            products,
            deviceProduct,
            productMatch,
            firmwareAntiRollback,
            steps,
            findings);
    }

    private static string? FindAndroidInfo(string root)
    {
        var candidates = new[]
        {
            Path.Combine(root, "android-info.txt"),
            Path.Combine(root, "images", "android-info.txt")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<int?> ReadAntiRollbackIndexAsync(string root, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, "images", "anti_version.txt");
        if (!File.Exists(path))
            return null;

        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 128 || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("ملف anti_version.txt غير صالح.");

        var value = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
        return int.TryParse(value, out var index) && index >= 0
            ? index
            : throw new InvalidDataException("محتوى anti_version.txt ليس رقمًا صحيحًا.");
    }

    private static string ResolveImagePath(string root, string token)
    {
        var normalized = token.Trim().Trim('"', '\'').Replace('/', '\\');
        if (normalized.Contains(':') || normalized.StartsWith("\\", StringComparison.Ordinal) ||
            normalized.Split('\\').Any(segment => segment is "." or ".."))
            throw new InvalidDataException("مسار صورة ROM مطلق أو يحتوي اجتياز مجلدات: " + token);

        var rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        normalized = normalized.Replace("%~dp0", rootPrefix, StringComparison.OrdinalIgnoreCase);
        if (normalized.Contains('%') || normalized.Contains('$'))
            throw new InvalidDataException("مسار صورة ROM يحتوي متغيرًا غير مدعوم: " + token);

        var fullPath = Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(root, normalized.Replace('\\', Path.DirectorySeparatorChar)));

        var imagesRoot = Path.GetFullPath(Path.Combine(root, "images"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(imagesRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            throw new InvalidDataException("ملف الصورة غير موجود داخل مجلد images أو مساره خارج الحزمة: " + token);

        var relative = Path.GetRelativePath(root, fullPath);
        var current = root;
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("مسار صورة ROM يحتوي رابطًا رمزيًا أو Junction: " + token);
        }

        return fullPath;
    }

    private static List<string> ReplaceSerialPlaceholders(IReadOnlyList<string> source)
    {
        var result = new List<string>();
        for (var index = 0; index < source.Count; index++)
        {
            var token = source[index];
            if (token.Equals("%*", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("$*", StringComparison.OrdinalIgnoreCase))
                continue;

            if (token.Equals("-s", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= source.Count ||
                    !(source[index + 1].Equals("%*", StringComparison.OrdinalIgnoreCase) ||
                      source[index + 1].Equals("$*", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("السكربت يحدد Serial ثابتًا؛ SoftBox يرفض استهداف جهاز غير المحدد.");
                index++;
                continue;
            }

            result.Add(token);
        }

        return result;
    }

    private static List<string> Tokenize(string command)
    {
        var matches = Regex.Matches(command, @"""([^""]|"""")*""|'[^']*'|\S+");
        return matches.Select(match => match.Value.Trim('"', '\'').Replace("\"\"", "\"")).ToList();
    }

    private static IEnumerable<string> JoinBatchContinuations(string text)
    {
        var pending = new StringBuilder();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd();
            var continued = line.EndsWith("^", StringComparison.Ordinal);
            if (continued)
                line = line[..^1];
            pending.Append(line);
            if (continued)
            {
                pending.Append(' ');
                continue;
            }

            yield return pending.ToString();
            pending.Clear();
        }

        if (pending.Length > 0)
            yield return pending.ToString();
    }
}
