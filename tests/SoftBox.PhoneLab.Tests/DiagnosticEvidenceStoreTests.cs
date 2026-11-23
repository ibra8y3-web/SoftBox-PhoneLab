using System.Text;
using SoftBox.PhoneLab.Core.Activity;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DiagnosticEvidenceStoreTests
{
    [Fact]
    public async Task EvidenceIsEncryptedMaskedAndDeduplicated()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "softbox-diagnostic-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var serial = "DEVICE-TEST-12345";
            var store = new DiagnosticEvidenceStore(root);
            var evidence = new[]
            {
                new ComponentDiagnostic
                {
                    Id = "touch",
                    Name = "طبقة اللمس",
                    Status = DiagnosticStatus.Pass,
                    Evidence = "Agent recorded complete touch-grid coverage."
                }
            };

            Assert.Equal(1, await store.RecordSnapshotAsync(serial, "Samsung", "SM-A315G", evidence, "PhoneLab Agent"));
            Assert.Equal(0, await store.RecordSnapshotAsync(serial, "Samsung", "SM-A315G", evidence, "PhoneLab Agent"));

            var raw = await File.ReadAllBytesAsync(store.DatabasePath);
            Assert.True(WindowsUserDataProtection.IsProtected(raw));
            Assert.DoesNotContain(Encoding.UTF8.GetBytes(serial), raw);

            var records = await store.GetRecentAsync(serial);
            var record = Assert.Single(records);
            Assert.Equal("••••2345", record.MaskedSerial);
            Assert.Equal("Pass", record.Status);
            Assert.Equal("touch", record.ComponentId);
            Assert.DoesNotContain(serial, record.DeviceFingerprint, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ChangedEvidenceIsStoredAsANewDiagnosticEvent()
    {
        if (!OperatingSystem.IsWindows()) return;

        var root = Path.Combine(Path.GetTempPath(), "softbox-diagnostic-change-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DiagnosticEvidenceStore(root);
            var serial = "DEVICE-TEST-98765";
            var first = new[]
            {
                new ComponentDiagnostic { Id = "battery", Name = "البطارية", Status = DiagnosticStatus.DataRead, Evidence = "Level: 95%" }
            };
            var second = new[]
            {
                new ComponentDiagnostic { Id = "battery", Name = "البطارية", Status = DiagnosticStatus.Review, Evidence = "Temperature requires review." }
            };

            Assert.Equal(1, await store.RecordSnapshotAsync(serial, "Samsung", "SM-A315G", first, "ADB"));
            Assert.Equal(1, await store.RecordSnapshotAsync(serial, "Samsung", "SM-A315G", second, "PhoneLab Agent"));

            var records = await store.GetRecentAsync(serial);
            Assert.Equal(2, records.Count);
            Assert.Equal("Review", records[0].Status);
            Assert.Equal("DataRead", records[1].Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
