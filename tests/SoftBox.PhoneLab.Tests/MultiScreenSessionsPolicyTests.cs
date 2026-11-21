using SoftBox.PhoneLab.Core.Security;
using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class MultiScreenSessionsPolicyTests
{
    [Theory]
    [InlineData("device", true)]
    [InlineData(" device ", true)]
    [InlineData("unauthorized", false)]
    [InlineData("offline", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ScrcpyStartRequiresFreshAuthorizedAdbGetState(string? state, bool expected)
        => Assert.Equal(expected, MultiScreenSessionsPolicy.IsAuthorizedAdbState(state));

    [Fact]
    public void ReturnsOnlyRequestedDevicesStillAuthorizedInFreshDiscovery()
    {
        var discovered = new[]
        {
            Device("ADB-ONE", "device"),
            Device("ADB-TWO", "unauthorized"),
            Device("ADB-THREE", "device"),
            Device("ADB-FOUR", "offline")
        };

        var selected = MultiScreenSessionsPolicy.SelectAuthorizedDevices(
            ["ADB-ONE", "ADB-TWO", "ADB-THREE", "ADB-FOUR", "ADB-MISSING"],
            discovered);

        Assert.Equal(new[] { "ADB-ONE", "ADB-THREE" }, selected.Select(device => device.Serial));
    }

    [Fact]
    public void DeduplicatesRequestedSerialsAndIgnoresUnsafeSerials()
    {
        var discovered = new[]
        {
            Device("ADB-ONE", "device"),
            Device("ADB-TWO", "device")
        };

        var selected = MultiScreenSessionsPolicy.SelectAuthorizedDevices(
            ["ADB-ONE", "ADB-ONE", "--wipe", "ADB-TWO"],
            discovered);

        Assert.Equal(new[] { "ADB-ONE", "ADB-TWO" }, selected.Select(device => device.Serial));
    }

    [Fact]
    public void RejectsMoreThanTheAvailableConcurrentSessionLimit()
    {
        var discovered = Enumerable.Range(1, 5)
            .Select(index => Device("ADB-" + index, "device"))
            .ToArray();
        var requested = discovered.Select(device => device.Serial).ToArray();

        var error = Assert.Throws<InvalidOperationException>(() =>
            MultiScreenSessionsPolicy.SelectAuthorizedDevices(requested, discovered));

        Assert.Contains("كحد أقصى", error.Message);
    }

    [Fact]
    public void EnforcesCurrentAvailableSlotCountWhenSomeSessionsAlreadyExist()
    {
        var discovered = Enumerable.Range(1, 4)
            .Select(index => Device("ADB-" + index, "device"))
            .ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            MultiScreenSessionsPolicy.SelectAuthorizedDevices(
                discovered.Select(device => device.Serial),
                discovered,
                maximumSessions: 2));
    }

    [Fact]
    public void ReadOnlyAccountCannotLaunchDeviceScreenSessions()
    {
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(
            SoftBoxUserRole.Administrator, "OpenMultiScreenSessions"));
        Assert.True(SoftBoxAuthorizationPolicy.IsAllowed(
            SoftBoxUserRole.Technician, "OpenMultiScreenSessions"));
        Assert.False(SoftBoxAuthorizationPolicy.IsAllowed(
            SoftBoxUserRole.ReadOnly, "OpenMultiScreenSessions"));
    }

    [Fact]
    public void HealthPolicyFindsDisconnectedProcessesAndDetachedWindowsOnly()
    {
        var unhealthy = MultiScreenSessionsPolicy.FindUnhealthySessions(
        [
            new("ADB-OK", true, true, true),
            new("ADB-OFFLINE", false, true, true),
            new("ADB-EXITED", true, false, true),
            new("ADB-DETACHED", true, true, false)
        ]);

        Assert.Equal(new[] { "ADB-OFFLINE", "ADB-EXITED", "ADB-DETACHED" }, unhealthy);
    }

    [Fact]
    public void HealthPolicyIgnoresDuplicateHealthySerialsAndRejectsUnsafeIdentifiers()
    {
        var unhealthy = MultiScreenSessionsPolicy.FindUnhealthySessions(
        [
            new("ADB-OK", true, true, true),
            new("ADB-OK", true, true, true),
            new("--kill", false, true, true)
        ]);

        Assert.Equal(new[] { "--kill" }, unhealthy);
    }

    private static DeviceListItem Device(string serial, string state)
        => new(serial, state, "Android", "Test device");
}
