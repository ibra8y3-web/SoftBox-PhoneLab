using SoftBox.PhoneLab.Core.Diagnostics;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidLogAnalysisEngineTests
{
    [Fact]
    public void ClassifiesCrashesAnrsStorageUsbAndSecurityEventsFromThreadtimeLogcat()
    {
        const string logcat = """
            11-21 12:00:00.000  1200  1200 E AndroidRuntime: FATAL EXCEPTION: main
            11-21 12:00:01.000  1200  1200 E ActivityManager: ANR in com.example.app
            11-21 12:00:02.000  1000  1000 W vold: No space left on device
            11-21 12:00:03.000  1000  1000 W auditd: avc: denied { read } for path=/data/system/example
            11-21 12:00:04.000  1000  1000 E UsbDeviceManager: USB disconnect
            """;

        var result = AndroidLogAnalysisEngine.Analyze(logcat);

        Assert.Equal(5, result.TotalLinesRead);
        Assert.Equal(5, result.RelevantLines);
        Assert.Contains(result.Events, item => item.Category == AndroidLogEventCategory.ApplicationCrash);
        Assert.Contains(result.Events, item => item.Category == AndroidLogEventCategory.ApplicationNotResponding);
        Assert.Contains(result.Events, item => item.Category == AndroidLogEventCategory.StorageAndFilesystem);
        Assert.Contains(result.Events, item => item.Category == AndroidLogEventCategory.SelinuxPolicy);
        Assert.Contains(result.Events, item =>
            item.Category == AndroidLogEventCategory.UsbAndConnectivity &&
            item.Severity == "Critical");
    }

    [Fact]
    public void AggregatesRepeatedEventsByCategoryAndTag()
    {
        const string logcat = """
            11-21 12:00:00.000  1200  1200 E AndroidRuntime: FATAL EXCEPTION: main
            11-21 12:01:00.000  1200  1200 E AndroidRuntime: FATAL EXCEPTION: main
            11-21 12:02:00.000  1200  1200 E AndroidRuntime: FATAL EXCEPTION: main
            """;

        var result = AndroidLogAnalysisEngine.Analyze(logcat);

        var item = Assert.Single(result.Events);
        Assert.Equal(AndroidLogEventCategory.ApplicationCrash, item.Category);
        Assert.Equal("AndroidRuntime", item.Tag);
        Assert.Equal(3, item.Occurrences);
        Assert.Equal("11-21 12:02:00.000", item.LastSeenTimestamp);
    }

    [Fact]
    public void RecognizesOutOfMemoryThermalAndWatchdogEvidence()
    {
        const string logcat = """
            11-21 12:00:00.000  1000  1000 E AndroidRuntime: java.lang.OutOfMemoryError: Failed to allocate
            11-21 12:00:01.000  1000  1000 W ThermalService: thermal status changed to severe
            11-21 12:00:02.000  1000  1000 E Watchdog: Watchdog detected system_server stall
            """;

        var result = AndroidLogAnalysisEngine.Analyze(logcat);

        Assert.Contains(result.Events, item => item.Category == AndroidLogEventCategory.MemoryPressure && item.Severity == "Critical");
        Assert.Contains(result.Events, item => item.Category == AndroidLogEventCategory.ThermalAndPower);
        Assert.Contains(result.Events, item => item.Category == AndroidLogEventCategory.AndroidSystem && item.Severity == "Critical");
    }

    [Fact]
    public void UnknownLogsProduceNoInventedFindings()
    {
        const string logcat = """
            11-21 12:00:00.000  1000  1000 I ActivityManager: Start proc com.example.app
            11-21 12:00:01.000  1000  1000 D AudioFlinger: normal audio route
            11-21 12:00:02.000  1000  1000 I SystemServer: boot completed normally
            """;

        var result = AndroidLogAnalysisEngine.Analyze(logcat);

        Assert.Empty(result.Events);
        Assert.Equal(0, result.RelevantLines);
        Assert.Contains(result.Limitations, item => item.Contains("لم يتم العثور", StringComparison.Ordinal));
    }

    [Fact]
    public void InputIsBoundedAndReportsTruncation()
    {
        var lines = Enumerable.Range(0, AndroidLogAnalysisEngine.MaximumAnalyzedLines + 50)
            .Select(index => $"11-21 12:00:00.000  1000  1000 I ActivityManager: normal event {index}");
        var result = AndroidLogAnalysisEngine.Analyze(string.Join(Environment.NewLine, lines));

        Assert.True(result.InputTruncated);
        Assert.Equal(AndroidLogAnalysisEngine.MaximumAnalyzedLines, result.LinesAnalyzed);
    }

    [Fact]
    public void AnalysisResultDoesNotStoreRawLogLinesOrPrivateMessageContents()
    {
        const string privateMarker = "private-user@example.test";
        var logcat = $"11-21 12:00:00.000  1000  1000 E AndroidRuntime: FATAL EXCEPTION {privateMarker}";

        var result = AndroidLogAnalysisEngine.Analyze(logcat);
        var serialized = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.DoesNotContain(privateMarker, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("FATAL EXCEPTION", serialized, StringComparison.Ordinal);
        Assert.Contains(result.Events, item => item.Category == AndroidLogEventCategory.ApplicationCrash);
    }

    [Fact]
    public void UnstructuredLinesCanBeClassifiedWithoutInventingTimestampOrTag()
    {
        var result = AndroidLogAnalysisEngine.Analyze("FATAL EXCEPTION: process died");

        var item = Assert.Single(result.Events);
        Assert.Equal("AndroidLog", item.Tag);
        Assert.Equal("", item.LastSeenTimestamp);
    }
}
