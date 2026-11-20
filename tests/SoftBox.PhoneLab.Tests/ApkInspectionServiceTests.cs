using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SoftBox.PhoneLab.Core.Apk;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class ApkInspectionServiceTests
{
    private readonly ApkInspectionService _service = new();

    [Fact]
    public async Task InspectAsyncReadsPlainXmlManifestAndComputesExactArchiveHash()
    {
        const string manifest = """
            <manifest xmlns:android="http://schemas.android.com/apk/res/android"
                      package="com.example.softbox"
                      android:versionCode="42"
                      android:versionName="2.1.0">
              <uses-sdk android:minSdkVersion="23" android:targetSdkVersion="35" />
              <uses-permission android:name="android.permission.INTERNET" />
              <application android:label="Demo &amp; Tools" />
            </manifest>
            """;

        var path = CreateApk(Encoding.UTF8.GetBytes(manifest), includeSignatureFiles: true);
        try
        {
            var result = await _service.InspectAsync(path);
            var fileBytes = await File.ReadAllBytesAsync(path);
            var expectedHash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();

            Assert.Equal("com.example.softbox", result.PackageName);
            Assert.Equal("Demo & Tools", result.ApplicationLabel);
            Assert.Equal("2.1.0", result.VersionName);
            Assert.Equal(42, result.VersionCode);
            Assert.Equal(23, result.MinSdkVersion);
            Assert.Equal(35, result.TargetSdkVersion);
            Assert.Contains("android.permission.INTERNET", result.Permissions);
            Assert.Equal(expectedHash, result.Sha256);
            Assert.Equal(ApkManifestFormat.PlainXml, result.ManifestFormat);
            Assert.True(result.ArchiveEntriesReadable);
            Assert.True(result.HasV1SignatureFiles);
            Assert.Contains(result.Warnings, warning => warning.Contains("لم يتم التحقق تشفيريًا", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InspectAsyncParsesCompiledAndroidBinaryXmlManifest()
    {
        var path = CreateApk(BuildBinaryManifest());
        try
        {
            var result = await _service.InspectAsync(path);

            Assert.Equal(ApkManifestFormat.AndroidBinaryXml, result.ManifestFormat);
            Assert.Equal("com.example.binary", result.PackageName);
            Assert.Equal("4.8", result.VersionName);
            Assert.Equal(73, result.VersionCode);
            Assert.Equal(24, result.MinSdkVersion);
            Assert.Equal(34, result.TargetSdkVersion);
            Assert.Equal("Camera Demo", result.ApplicationLabel);
            Assert.Contains("android.permission.CAMERA", result.Permissions);
            Assert.Matches("^[0-9a-f]{64}$", result.Sha256);
            Assert.True(result.ArchiveEntriesReadable);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InspectAsyncReportsMissingManifestWithoutInventingMetadata()
    {
        var path = CreateApk(manifest: null);
        try
        {
            var result = await _service.InspectAsync(path);

            Assert.True(result.ArchiveEntriesReadable);
            Assert.Null(result.PackageName);
            Assert.Null(result.VersionName);
            Assert.Equal(ApkManifestFormat.Unavailable, result.ManifestFormat);
            Assert.Contains(result.Warnings, warning => warning.Contains("AndroidManifest.xml", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InspectAsyncRejectsCorruptZipData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"softbox-corrupt-{Guid.NewGuid():N}.apk");
        await File.WriteAllTextAsync(path, "this is not an APK or ZIP archive");
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => _service.InspectAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InspectAsyncRejectsNonApkFileExtension()
    {
        var path = Path.Combine(Path.GetTempPath(), $"softbox-not-apk-{Guid.NewGuid():N}.zip");
        await File.WriteAllTextAsync(path, "not used");
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => _service.InspectAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CompareAsyncReportsVersionPermissionAndArchiveEntryChanges()
    {
        const string firstManifest = """
            <manifest xmlns:android="http://schemas.android.com/apk/res/android"
                      package="com.example.compare"
                      android:versionCode="10"
                      android:versionName="1.0">
              <uses-sdk android:minSdkVersion="23" android:targetSdkVersion="34" />
              <uses-permission android:name="android.permission.INTERNET" />
              <application android:label="Compare Demo" />
            </manifest>
            """;
        const string secondManifest = """
            <manifest xmlns:android="http://schemas.android.com/apk/res/android"
                      package="com.example.compare"
                      android:versionCode="11"
                      android:versionName="2.0">
              <uses-sdk android:minSdkVersion="23" android:targetSdkVersion="35" />
              <uses-permission android:name="android.permission.CAMERA" />
              <application android:label="Compare Demo" />
            </manifest>
            """;

        var firstPath = CreateApk(Encoding.UTF8.GetBytes(firstManifest));
        var secondPath = CreateApk(Encoding.UTF8.GetBytes(secondManifest), extraEntry: "res/xml/new_settings.xml");
        try
        {
            var result = await _service.CompareAsync(firstPath, secondPath);

            Assert.True(result.SamePackage);
            Assert.True(result.VersionNameChanged);
            Assert.True(result.VersionCodeChanged);
            Assert.False(result.Sha256Matches);
            Assert.True(result.EntryListsComplete);
            Assert.Contains("android.permission.CAMERA", result.AddedPermissions);
            Assert.Contains("android.permission.INTERNET", result.RemovedPermissions);
            Assert.Contains("res/xml/new_settings.xml", result.AddedArchiveEntries);
            Assert.Empty(result.RemovedArchiveEntries);
            Assert.Contains(result.Warnings, warning => warning.Contains("محتوى كل ملف", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task CompareAsyncWarnsWhenPackageNamesDiffer()
    {
        const string firstManifest = "<manifest package=\"com.example.first\" />";
        const string secondManifest = "<manifest package=\"com.example.second\" />";
        var firstPath = CreateApk(Encoding.UTF8.GetBytes(firstManifest));
        var secondPath = CreateApk(Encoding.UTF8.GetBytes(secondManifest));
        try
        {
            var result = await _service.CompareAsync(firstPath, secondPath);

            Assert.False(result.SamePackage);
            Assert.Contains(result.Warnings, warning => warning.Contains("اسما الحزمة مختلفان", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    private static string CreateApk(byte[]? manifest, bool includeSignatureFiles = false, string? extraEntry = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"softbox-apk-{Guid.NewGuid():N}.apk");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false);

        if (manifest is not null)
            WriteEntry(archive, "AndroidManifest.xml", manifest);

        WriteEntry(archive, "classes.dex", [0x64, 0x65, 0x78, 0x0A]);
        WriteEntry(archive, "res/raw/payload.bin", [0x01, 0x02, 0x03, 0x04]);
        if (!string.IsNullOrWhiteSpace(extraEntry))
            WriteEntry(archive, extraEntry, [0x05, 0x06, 0x07]);

        if (includeSignatureFiles)
        {
            WriteEntry(archive, "META-INF/CERT.SF", Encoding.ASCII.GetBytes("signature metadata fixture"));
            WriteEntry(archive, "META-INF/CERT.RSA", [0x30, 0x00]);
        }

        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static byte[] BuildBinaryManifest()
    {
        var values = new[]
        {
            "manifest", "package", "com.example.binary", "versionCode", "versionName", "4.8",
            "uses-sdk", "minSdkVersion", "targetSdkVersion", "uses-permission", "name",
            "android.permission.CAMERA", "application", "label", "Camera Demo"
        };
        var index = values.Select((value, position) => (value, position))
            .ToDictionary(item => item.value, item => (uint)item.position, StringComparer.Ordinal);

        var chunks = new List<byte[]>
        {
            BuildStringPool(values),
            BuildStartElement("manifest",
            [
                StringAttribute("package", "com.example.binary", index),
                IntegerAttribute("versionCode", 73, index),
                StringAttribute("versionName", "4.8", index)
            ], index),
            BuildStartElement("uses-sdk",
            [
                IntegerAttribute("minSdkVersion", 24, index),
                IntegerAttribute("targetSdkVersion", 34, index)
            ], index),
            BuildStartElement("uses-permission",
            [
                StringAttribute("name", "android.permission.CAMERA", index)
            ], index),
            BuildStartElement("application",
            [
                StringAttribute("label", "Camera Demo", index)
            ], index)
        };

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0x0003);
        writer.Write((ushort)8);
        writer.Write((uint)(8 + chunks.Sum(chunk => chunk.Length)));
        foreach (var chunk in chunks)
            writer.Write(chunk);
        writer.Flush();
        return output.ToArray();
    }

    private static byte[] BuildStringPool(IReadOnlyList<string> values)
    {
        var offsets = new List<uint>();
        using var strings = new MemoryStream();
        using (var stringWriter = new BinaryWriter(strings, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var value in values)
            {
                offsets.Add((uint)strings.Position);
                var encoded = Encoding.UTF8.GetBytes(value);
                WriteLength8(stringWriter, value.Length);
                WriteLength8(stringWriter, encoded.Length);
                stringWriter.Write(encoded);
                stringWriter.Write((byte)0);
            }
        }

        var stringsStart = 28 + (values.Count * 4);
        var chunkSize = stringsStart + (int)strings.Length;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0x0001);
        writer.Write((ushort)28);
        writer.Write((uint)chunkSize);
        writer.Write((uint)values.Count);
        writer.Write((uint)0);
        writer.Write((uint)0x00000100);
        writer.Write((uint)stringsStart);
        writer.Write((uint)0);
        foreach (var offset in offsets)
            writer.Write(offset);
        writer.Write(strings.ToArray());
        writer.Flush();
        return output.ToArray();
    }

    private static void WriteLength8(BinaryWriter writer, int length)
    {
        if (length < 0x80)
        {
            writer.Write((byte)length);
            return;
        }

        writer.Write((byte)((length >> 8) | 0x80));
        writer.Write((byte)(length & 0xFF));
    }

    private static byte[] BuildStartElement(
        string elementName,
        IReadOnlyList<BinaryAttribute> attributes,
        IReadOnlyDictionary<string, uint> index)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        var chunkSize = 8 + 8 + 20 + (attributes.Count * 20);
        writer.Write((ushort)0x0102);
        writer.Write((ushort)16);
        writer.Write((uint)chunkSize);
        writer.Write((uint)1); // line number
        writer.Write(uint.MaxValue); // comment
        writer.Write(uint.MaxValue); // element namespace
        writer.Write(index[elementName]);
        writer.Write((ushort)20); // attributeStart relative to attrExt
        writer.Write((ushort)20); // attributeSize
        writer.Write((ushort)attributes.Count);
        writer.Write((ushort)0); // idIndex
        writer.Write((ushort)0); // classIndex
        writer.Write((ushort)0); // styleIndex

        foreach (var attribute in attributes)
        {
            writer.Write(uint.MaxValue); // namespace
            writer.Write(index[attribute.Name]);
            writer.Write(attribute.RawValueIndex);
            writer.Write((ushort)8); // typed value size
            writer.Write((byte)0);
            writer.Write(attribute.DataType);
            writer.Write(attribute.Data);
        }

        writer.Flush();
        return output.ToArray();
    }

    private static BinaryAttribute StringAttribute(
        string name,
        string value,
        IReadOnlyDictionary<string, uint> index)
    {
        var valueIndex = index[value];
        return new BinaryAttribute(name, valueIndex, 0x03, valueIndex);
    }

    private static BinaryAttribute IntegerAttribute(
        string name,
        uint value,
        IReadOnlyDictionary<string, uint> index)
        => new(name, uint.MaxValue, 0x10, value);

    private sealed record BinaryAttribute(string Name, uint RawValueIndex, byte DataType, uint Data);
}
