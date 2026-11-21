using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SoftBoxDesktopTelemetryTests
{
    [Fact]
    public void CreatesStructuredCrashEntryWithExplicitUtcTimestampAndProcessId()
    {
        var timestamp = new DateTimeOffset(2026, 11, 21, 12, 30, 0, TimeSpan.FromHours(2));
        var exception = new InvalidOperationException("desktop test failure");
        var entry = SoftBoxDesktopTelemetry.CreateEntry(
            "wpf-dispatcher-unhandled",
            exception,
            isTerminating: true,
            timestampUtc: timestamp);

        Assert.Equal(timestamp.ToUniversalTime(), entry.TimestampUtc);
        Assert.Equal("wpf-dispatcher-unhandled", entry.Event);
        Assert.Equal("SoftBox.PhoneLab.Desktop", entry.Source);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.ExceptionType);
        Assert.Equal("desktop test failure", entry.Message);
        Assert.True(entry.IsTerminating);
        Assert.True(entry.ProcessId > 0);
    }

    [Fact]
    public void StructuredEntryBoundsVeryLongExceptionText()
    {
        var exception = new InvalidOperationException(new string('X', 8000));
        var entry = SoftBoxDesktopTelemetry.CreateEntry("overflow", exception, false);

        Assert.True(entry.Message.Length <= 2000);
        Assert.True(entry.ExceptionType.Length <= 240);
        Assert.True(entry.Event.Length <= 80);
    }

    [Fact]
    public void StartupEventCanBeCreatedWithoutPersistingAnExceptionStack()
    {
        var entry = SoftBoxDesktopTelemetry.CreateEntry(
            "desktop-started",
            new InvalidOperationException("started"),
            false,
            includeStack: false);

        Assert.Empty(entry.StackTrace);
        Assert.Equal("started", entry.Message);
    }
}
