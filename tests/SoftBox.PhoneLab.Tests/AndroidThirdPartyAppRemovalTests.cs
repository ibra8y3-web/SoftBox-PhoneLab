using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidThirdPartyAppRemovalTests
{
    private const string Device = "R58M123456A";

    [Fact]
    public async Task RemovesOnlyPackageReturnedByThirdPartyPackageList()
    {
        var adb = new FakeAdbCommandRunner(
            "package:com.example.customerapp\npackage:org.sample.tool\n",
            "Success");
        var service = new AndroidDeviceToolsService(adb);

        var result = await service.UninstallThirdPartyPackageAsync(Device, "com.example.customerapp");

        Assert.Equal("Success", result);
        Assert.Collection(adb.Calls,
            call => Assert.Equal(
                new[] { "-s", Device, "shell", "pm", "list", "packages", "-3" }, call),
            call => Assert.Equal(
                new[] { "-s", Device, "uninstall", "com.example.customerapp" }, call));
    }

    [Fact]
    public async Task RefusesPackageThatIsNotInThirdPartyList()
    {
        var adb = new FakeAdbCommandRunner("package:com.example.customerapp\n");
        var service = new AndroidDeviceToolsService(adb);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UninstallThirdPartyPackageAsync(Device, "com.android.settings"));

        Assert.Single(adb.Calls);
        Assert.Equal("pm", adb.Calls[0][4]);
    }

    [Fact]
    public async Task DoesNotReportSuccessWhenAdbDoesNotReturnSuccess()
    {
        var adb = new FakeAdbCommandRunner(
            "package:com.example.customerapp\n",
            "Failure [DELETE_FAILED_INTERNAL_ERROR]");
        var service = new AndroidDeviceToolsService(adb);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UninstallThirdPartyPackageAsync(Device, "com.example.customerapp"));

        Assert.Equal(2, adb.Calls.Count);
    }

    [Theory]
    [InlineData("not-a-package")]
    [InlineData("com.example;pm")]
    [InlineData("com.example.app/../../settings")]
    public async Task RejectsInvalidPackageNamesBeforeCallingAdb(string packageName)
    {
        var adb = new FakeAdbCommandRunner();
        var service = new AndroidDeviceToolsService(adb);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UninstallThirdPartyPackageAsync(Device, packageName));

        Assert.Empty(adb.Calls);
    }

    [Fact]
    public async Task RejectsInvalidDeviceSerialBeforeCallingAdb()
    {
        var adb = new FakeAdbCommandRunner();
        var service = new AndroidDeviceToolsService(adb);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UninstallThirdPartyPackageAsync("-s", "com.example.customerapp"));

        Assert.Empty(adb.Calls);
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
