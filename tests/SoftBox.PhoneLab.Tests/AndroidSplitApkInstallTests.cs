using System.IO.Compression;
using System.Text;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidSplitApkInstallTests
{
    private const string Device = "R58M123456A";

    [Fact]
    public async Task InspectsTheSetAndOrdersBaseBeforeAlphabeticalSplits()
    {
        var baseApk = CreateApk("com.example.splitapp", 42, null, "3.2.1");
        var armApk = CreateApk("com.example.splitapp", 42, "config.arm64_v8a", "3.2.1");
        var densityApk = CreateApk("com.example.splitapp", 42, "config.xxhdpi", "3.2.1");
        try
        {
            var service = new AndroidDeviceToolsService(new FakeAdbCommandRunner());
            var plan = await service.PrepareSplitApkInstallAsync([densityApk, armApk, baseApk]);

            Assert.Equal("com.example.splitapp", plan.PackageName);
            Assert.Equal("3.2.1", plan.VersionName);
            Assert.Equal(42, plan.VersionCode);
            Assert.Equal(new string?[] { null, "config.arm64_v8a", "config.xxhdpi" },
                plan.Files.Select(file => file.SplitName));
            Assert.Equal(Path.GetFullPath(baseApk), plan.Files[0].FullPath);
            Assert.All(plan.Files, file => Assert.Matches("^[0-9a-f]{64}$", file.Sha256));
        }
        finally
        {
            DeleteFiles(baseApk, armApk, densityApk);
        }
    }

    [Fact]
    public async Task RejectsPackageNameMismatchBeforeCallingAdb()
    {
        var baseApk = CreateApk("com.example.first", 10, null);
        var splitApk = CreateApk("com.example.second", 10, "config.arm64_v8a");
        try
        {
            var adb = new FakeAdbCommandRunner();
            var service = new AndroidDeviceToolsService(adb);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.PrepareSplitApkInstallAsync([baseApk, splitApk]));

            Assert.Empty(adb.Calls);
        }
        finally
        {
            DeleteFiles(baseApk, splitApk);
        }
    }

    [Fact]
    public async Task RejectsVersionCodeMismatchBeforeCallingAdb()
    {
        var baseApk = CreateApk("com.example.splitapp", 10, null);
        var splitApk = CreateApk("com.example.splitapp", 11, "config.arm64_v8a");
        try
        {
            var service = new AndroidDeviceToolsService(new FakeAdbCommandRunner());

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.PrepareSplitApkInstallAsync([baseApk, splitApk]));
        }
        finally
        {
            DeleteFiles(baseApk, splitApk);
        }
    }

    [Fact]
    public async Task RejectsMultipleBaseApksAndMissingBaseApk()
    {
        var baseOne = CreateApk("com.example.splitapp", 12, null);
        var baseTwo = CreateApk("com.example.splitapp", 12, null);
        var splitOne = CreateApk("com.example.splitapp", 12, "config.arm64_v8a");
        var splitTwo = CreateApk("com.example.splitapp", 12, "config.xxhdpi");
        try
        {
            var service = new AndroidDeviceToolsService(new FakeAdbCommandRunner());

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.PrepareSplitApkInstallAsync([baseOne, baseTwo]));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.PrepareSplitApkInstallAsync([splitOne, splitTwo]));
        }
        finally
        {
            DeleteFiles(baseOne, baseTwo, splitOne, splitTwo);
        }
    }

    [Fact]
    public async Task UsesInstallMultipleWithSelectedSerialAndBaseFirst()
    {
        var baseApk = CreateApk("com.example.splitapp", 25, null);
        var splitApk = CreateApk("com.example.splitapp", 25, "config.arm64_v8a");
        try
        {
            var adb = new FakeAdbCommandRunner("Success\n");
            var service = new AndroidDeviceToolsService(adb);
            var plan = await service.PrepareSplitApkInstallAsync([splitApk, baseApk]);

            var output = await service.InstallSplitApkSetAsync(Device, plan);

            Assert.Equal("Success", output);
            Assert.Collection(adb.Calls,
                call => Assert.Equal(new[]
                {
                    "-s", Device, "install-multiple", "-r",
                    Path.GetFullPath(baseApk), Path.GetFullPath(splitApk)
                }, call));
        }
        finally
        {
            DeleteFiles(baseApk, splitApk);
        }
    }

    [Fact]
    public async Task RefusesAnyFileChangedAfterPreflightBeforeRunningAdb()
    {
        var baseApk = CreateApk("com.example.splitapp", 25, null);
        var splitApk = CreateApk("com.example.splitapp", 25, "config.arm64_v8a");
        try
        {
            var adb = new FakeAdbCommandRunner("Success");
            var service = new AndroidDeviceToolsService(adb);
            var plan = await service.PrepareSplitApkInstallAsync([baseApk, splitApk]);
            await File.AppendAllTextAsync(splitApk, "changed after inspection");

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.InstallSplitApkSetAsync(Device, plan));

            Assert.Empty(adb.Calls);
        }
        finally
        {
            DeleteFiles(baseApk, splitApk);
        }
    }

    [Fact]
    public async Task DoesNotReportInstallSuccessWithoutExplicitAdbSuccessLine()
    {
        var baseApk = CreateApk("com.example.splitapp", 25, null);
        var splitApk = CreateApk("com.example.splitapp", 25, "config.arm64_v8a");
        try
        {
            var adb = new FakeAdbCommandRunner("Failure [INSTALL_FAILED_INVALID_APK]");
            var service = new AndroidDeviceToolsService(adb);
            var plan = await service.PrepareSplitApkInstallAsync([baseApk, splitApk]);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.InstallSplitApkSetAsync(Device, plan));

            Assert.Single(adb.Calls);
        }
        finally
        {
            DeleteFiles(baseApk, splitApk);
        }
    }

    [Fact]
    public async Task RejectsInvalidDeviceSerialBeforeCallingAdb()
    {
        var baseApk = CreateApk("com.example.splitapp", 25, null);
        var splitApk = CreateApk("com.example.splitapp", 25, "config.arm64_v8a");
        try
        {
            var adb = new FakeAdbCommandRunner("Success");
            var service = new AndroidDeviceToolsService(adb);
            var plan = await service.PrepareSplitApkInstallAsync([baseApk, splitApk]);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.InstallSplitApkSetAsync("-s", plan));

            Assert.Empty(adb.Calls);
        }
        finally
        {
            DeleteFiles(baseApk, splitApk);
        }
    }

    private static string CreateApk(string packageName, int versionCode, string? splitName, string versionName = "1.0")
    {
        var path = Path.Combine(Path.GetTempPath(), $"softbox-split-{Guid.NewGuid():N}.apk");
        var splitAttribute = splitName is null ? "" : $" split=\"{splitName}\"";
        var manifest = $"<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"{packageName}\"{splitAttribute} android:versionCode=\"{versionCode}\" android:versionName=\"{versionName}\"><application android:label=\"Split test\" /></manifest>";

        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false);
        WriteEntry(archive, "AndroidManifest.xml", Encoding.UTF8.GetBytes(manifest));
        WriteEntry(archive, "classes.dex", [0x64, 0x65, 0x78, 0x0A]);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static void DeleteFiles(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class FakeAdbCommandRunner(params string[] outputs) : IAdbCommandRunner
    {
        private readonly Queue<string> _outputs = new(outputs);
        public List<string[]> Calls { get; } = [];
        public bool IsAvailable => true;

        public Task<string> RunAsync(
            IReadOnlyList<string> args,
            CancellationToken cancellationToken,
            TimeSpan? commandTimeout = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(args.ToArray());
            return Task.FromResult(_outputs.Count > 0 ? _outputs.Dequeue() : "");
        }
    }
}
