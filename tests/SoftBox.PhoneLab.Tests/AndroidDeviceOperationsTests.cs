using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidDeviceOperationsTests
{
    private const string Device = "R58M123456A";
    private const string Package = "com.example.customerapp";
    private const string Camera = "android.permission.CAMERA";

    [Fact]
    public async Task GrantsRuntimePermissionAndVerifiesPackageManagerStateAfterward()
    {
        var adb = new FakeAdbCommandRunner(
            $"package:{Package}\n",
            PackageDump(granted: false),
            "",
            $"package:{Package}\n",
            PackageDump(granted: true));
        var service = new AndroidDeviceToolsService(adb);

        var result = await service.SetThirdPartyRuntimePermissionAsync(Device, Package, Camera, granted: true);

        Assert.Contains("تم منح", result);
        Assert.Equal(5, adb.Calls.Count);
        Assert.Equal(new[] { "-s", Device, "shell", "pm", "grant", Package, Camera }, adb.Calls[2]);
    }

    [Fact]
    public async Task RevokesRuntimePermissionAndVerifiesTheNewState()
    {
        var adb = new FakeAdbCommandRunner(
            $"package:{Package}\n",
            PackageDump(granted: true),
            "",
            $"package:{Package}\n",
            PackageDump(granted: false));
        var service = new AndroidDeviceToolsService(adb);

        var result = await service.SetThirdPartyRuntimePermissionAsync(Device, Package, Camera, granted: false);

        Assert.Contains("تم سحب", result);
        Assert.Equal(new[] { "-s", Device, "shell", "pm", "revoke", Package, Camera }, adb.Calls[2]);
    }

    [Fact]
    public async Task DoesNotSendGrantWhenPermissionIsNotRequestedByTheSelectedPackage()
    {
        var adb = new FakeAdbCommandRunner(
            $"package:{Package}\n",
            PackageDump(granted: false));
        var service = new AndroidDeviceToolsService(adb);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SetThirdPartyRuntimePermissionAsync(Device, Package, "android.permission.READ_SMS", true));

        Assert.Equal(2, adb.Calls.Count);
    }

    [Fact]
    public async Task DoesNotSendGrantWhenPackageIsNotThirdParty()
    {
        var adb = new FakeAdbCommandRunner("package:com.example.other\n");
        var service = new AndroidDeviceToolsService(adb);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SetThirdPartyRuntimePermissionAsync(Device, "com.android.settings", Camera, true));

        Assert.Single(adb.Calls);
    }

    [Fact]
    public async Task DoesNotSendMutationWhenPackageManagerReportsConflictingPermissionState()
    {
        const string conflicting = """
            requested permissions:
              android.permission.CAMERA
            runtime permissions:
              android.permission.CAMERA: granted=false, flags=[ USER_SET ]
              android.permission.CAMERA: granted=true, flags=[ USER_SET ]
            """;
        var adb = new FakeAdbCommandRunner($"package:{Package}\n", conflicting);
        var service = new AndroidDeviceToolsService(adb);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.SetThirdPartyRuntimePermissionAsync(Device, Package, Camera, true));

        Assert.Equal(2, adb.Calls.Count);
    }

    [Fact]
    public async Task DoesNotClaimPermissionWasChangedWhenPostOperationVerificationFails()
    {
        var adb = new FakeAdbCommandRunner(
            $"package:{Package}\n",
            PackageDump(granted: false),
            "",
            $"package:{Package}\n",
            PackageDump(granted: false));
        var service = new AndroidDeviceToolsService(adb);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetThirdPartyRuntimePermissionAsync(Device, Package, Camera, granted: true));
    }

    [Fact]
    public async Task ClearsOnlyThirdPartyAppDataAndRequiresAndroidSuccessMarker()
    {
        var adb = new FakeAdbCommandRunner(
            $"package:{Package}\n",
            "Success",
            $"package:{Package}\n");
        var service = new AndroidDeviceToolsService(adb);

        var result = await service.ClearThirdPartyPackageDataAsync(Device, Package);

        Assert.Contains("مسح بيانات التطبيق", result);
        Assert.Equal(3, adb.Calls.Count);
        Assert.Equal(new[] { "-s", Device, "shell", "pm", "clear", Package }, adb.Calls[1]);
    }

    [Fact]
    public async Task DoesNotClaimDataClearWhenAndroidDoesNotConfirmSuccess()
    {
        var adb = new FakeAdbCommandRunner($"package:{Package}\n", "Failed");
        var service = new AndroidDeviceToolsService(adb);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ClearThirdPartyPackageDataAsync(Device, Package));
        Assert.Equal(2, adb.Calls.Count);
    }

    [Fact]
    public async Task LaunchesOnlyARevalidatedThirdPartyPackage()
    {
        var adb = new FakeAdbCommandRunner($"package:{Package}\n", "Events injected: 1");
        var service = new AndroidDeviceToolsService(adb);

        var result = await service.LaunchThirdPartyPackageAsync(Device, Package);

        Assert.Contains("Events injected", result);
        Assert.Equal(new[]
        {
            "-s", Device, "shell", "monkey", "-p", Package,
            "-c", "android.intent.category.LAUNCHER", "1"
        }, adb.Calls[1]);
    }

    [Fact]
    public async Task ForceStopsOnlyARevalidatedThirdPartyPackage()
    {
        var adb = new FakeAdbCommandRunner($"package:{Package}\n", "");
        var service = new AndroidDeviceToolsService(adb);

        var result = await service.ForceStopThirdPartyPackageAsync(Device, Package);

        Assert.Contains(Package, result);
        Assert.Equal(new[] { "-s", Device, "shell", "am", "force-stop", Package }, adb.Calls[1]);
    }

    [Theory]
    [InlineData(AndroidRebootTarget.System, new[] { "-s", Device, "reboot" })]
    [InlineData(AndroidRebootTarget.Recovery, new[] { "-s", Device, "reboot", "recovery" })]
    [InlineData(AndroidRebootTarget.Bootloader, new[] { "-s", Device, "reboot", "bootloader" })]
    public async Task RebootsTheSelectedAuthorizedDeviceIntoOnlyTheSelectedTarget(
        AndroidRebootTarget target,
        string[] expected)
    {
        var adb = new FakeAdbCommandRunner("device", "");
        var service = new AndroidDeviceToolsService(adb);

        await service.RebootDeviceAsync(Device, target);

        Assert.Equal(2, adb.Calls.Count);
        Assert.Equal(new[] { "-s", Device, "get-state" }, adb.Calls[0]);
        Assert.Equal(expected, adb.Calls[1]);
    }

    [Fact]
    public async Task DoesNotRebootWhenAdbStateIsNotAuthorizedDevice()
    {
        var adb = new FakeAdbCommandRunner("unauthorized");
        var service = new AndroidDeviceToolsService(adb);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RebootDeviceAsync(Device, AndroidRebootTarget.Recovery));

        Assert.Single(adb.Calls);
    }

    [Fact]
    public async Task RejectsInvalidDeviceAndPackageBeforeAnyCommand()
    {
        var adb = new FakeAdbCommandRunner();
        var service = new AndroidDeviceToolsService(adb);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RebootDeviceAsync("-s", AndroidRebootTarget.System));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ClearThirdPartyPackageDataAsync(Device, "com.example;pm"));

        Assert.Empty(adb.Calls);
    }

    private static string PackageDump(bool granted) =>
        $"""
        Packages:
          Package [{Package}]
            requested permissions:
              {Camera}
              android.permission.RECORD_AUDIO
            install permissions:
              android.permission.INTERNET: granted=true, flags=[ ]
            runtime permissions:
              {Camera}: granted={granted.ToString().ToLowerInvariant()}, flags=[ USER_SET|USER_SENSITIVE_WHEN_GRANTED ]
              android.permission.RECORD_AUDIO: granted=true, flags=[ USER_SET ]
        """;

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
