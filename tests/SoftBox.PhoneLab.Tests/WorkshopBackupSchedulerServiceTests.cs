using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using SoftBox.PhoneLab.Core.Security;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class WorkshopBackupSchedulerServiceTests
{
    [Fact]
    public void TaskDefinitionUsesDailyInteractiveLeastPrivilegeAndStartWhenAvailable()
    {
        var now = new DateTime(2026, 11, 20, 23, 55, 0, DateTimeKind.Local);
        var source = Path.GetFullPath(@"C:\SoftBox Data\workshop.json");
        var destination = Path.GetFullPath(@"D:\SoftBox Backups");
        var xml = WorkshopBackupSchedulerService.BuildTaskDefinitionXml(
            "S-1-5-21-1000",
            source,
            destination,
            new TimeOnly(2, 30),
            30,
            now);

        var document = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Equal("S-1-5-21-1000", document.Root!.Element(ns + "Principals")!
            .Element(ns + "Principal")!.Element(ns + "UserId")!.Value);
        Assert.Equal("InteractiveToken", document.Descendants(ns + "LogonType").Single().Value);
        Assert.Equal("LeastPrivilege", document.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal("true", document.Descendants(ns + "StartWhenAvailable").Single().Value);
        Assert.Equal("1", document.Descendants(ns + "DaysInterval").Single().Value);
        Assert.Equal("2026-11-21T02:30:00",
            document.Descendants(ns + "StartBoundary").Single().Value);

        var arguments = document.Descendants(ns + "Arguments").Single().Value;
        var encoded = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        Assert.Contains("SBXDPAPI1", script);
        Assert.Contains("SoftBox-Workshop-Backup-*.sbxbackup", script);
        Assert.Contains("$retentionDays = 30", script);
        Assert.Contains("[IO.File]::WriteAllBytes($temporary, $sourceBytes)", script);
        Assert.Contains("[IO.File]::Delete($candidate)", script);
        Assert.DoesNotContain("Unprotect", script);
    }

    [Fact]
    public void PowerShellPathsAreEscapedAsSingleQuotedLiterals()
    {
        var script = WorkshopBackupSchedulerService.BuildBackupScript(
            @"C:\Repair Center\Owner's workshop.json",
            @"D:\Owner's Backups",
            14);

        Assert.Contains(@"Owner''s workshop.json", script);
        Assert.Contains(@"Owner''s Backups", script);
        Assert.Contains("$retentionDays = 14", script);
    }

    [Fact]
    public async Task ConfigureCreatesTaskUsingArgumentListAndDeletesTemporaryDefinition()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "workshop.json");
            var protectedBytes = WindowsUserDataProtection.Protect(
                Encoding.UTF8.GetBytes("""{"SchemaVersion":5,"Customers":[]}"""),
                "WorkshopStore.v1");
            await File.WriteAllBytesAsync(source, protectedBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);

            var runner = new FakeTaskSchedulerRunner();
            var temp = Path.Combine(root, "xml");
            var scheduler = new WorkshopBackupSchedulerService(
                runner,
                () => "S-1-5-21-1000",
                temp);
            var destination = Path.Combine(root, "backups");

            await scheduler.ConfigureDailyBackupAsync(
                source, destination, new TimeOnly(1, 45), 7);

            Assert.Equal("/Create", runner.Arguments[0]);
            Assert.Equal(WorkshopBackupSchedulerService.ScheduledTaskName, runner.Arguments[2]);
            Assert.Contains("/XML", runner.Arguments);
            Assert.Contains("/F", runner.Arguments);
            Assert.NotNull(runner.CapturedXml);
            Assert.False(File.Exists(runner.XmlPath!));
            Assert.True(Directory.Exists(destination));
            Assert.Contains("StartWhenAvailable", runner.CapturedXml);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ConfigureRejectsPlaintextOrUnprotectedWorkshopFiles()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "workshop.json");
            await File.WriteAllTextAsync(source, """{"SchemaVersion":5}""");
            var runner = new FakeTaskSchedulerRunner();
            var scheduler = new WorkshopBackupSchedulerService(
                runner, () => "S-1-5-21-1000", Path.Combine(root, "xml"));

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                scheduler.ConfigureDailyBackupAsync(
                    source, Path.Combine(root, "backups"), new TimeOnly(2, 0), 30));
            Assert.Empty(runner.Arguments);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ConfigureReportsTaskSchedulerFailures()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "workshop.json");
            var protectedBytes = WindowsUserDataProtection.Protect(
                Encoding.UTF8.GetBytes("""{"SchemaVersion":5}"""),
                "WorkshopStore.v1");
            await File.WriteAllBytesAsync(source, protectedBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);

            var runner = new FakeTaskSchedulerRunner { Result = new TaskSchedulerCommandResult(1, "", "Access denied") };
            var scheduler = new WorkshopBackupSchedulerService(
                runner, () => "S-1-5-21-1000", Path.Combine(root, "xml"));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scheduler.ConfigureDailyBackupAsync(
                    source, Path.Combine(root, "backups"), new TimeOnly(2, 0), 30));
            Assert.Contains("Access denied", error.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-backup-scheduler-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeTaskSchedulerRunner : ITaskSchedulerCommandRunner
    {
        public TaskSchedulerCommandResult Result { get; set; } = new(0, "SUCCESS", "");
        public List<string> Arguments { get; } = [];
        public string? XmlPath { get; private set; }
        public string? CapturedXml { get; private set; }

        public async Task<TaskSchedulerCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Arguments.AddRange(arguments);
            var xmlIndex = arguments.ToList().FindIndex(value => value.Equals("/XML", StringComparison.OrdinalIgnoreCase));
            if (xmlIndex >= 0 && xmlIndex + 1 < arguments.Count)
            {
                XmlPath = arguments[xmlIndex + 1];
                CapturedXml = await File.ReadAllTextAsync(XmlPath, cancellationToken);
            }
            return Result;
        }
    }
}
