using SoftBox.PhoneLab.Core.Help;
using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class RepairKnowledgeStoreTests
{
    [Fact]
    public async Task AddsSearchesAndDeletesTechnicianAuthoredKnowledge()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new RepairKnowledgeStore(root);
            var entry = await store.AddAsync(
                "Samsung", "SM-A546E", "لا يشحن", "تم قياس هبوط جهد على خط الشحن",
                "قراءة ملتيميتر من الفني في الورشة",
                ["افحص الكابل ومصدر الطاقة", "قِس VBUS بمسبار معزول حسب دليل اللوحة"],
                "Service manual page 8", "Technician A", true);

            Assert.True(File.Exists(store.DatabasePath));
            Assert.True(WindowsUserDataProtection.IsProtected(await File.ReadAllBytesAsync(store.DatabasePath)));
            var result = await store.SearchAsync("VBUS", "Samsung", "A546E");
            Assert.Single(result);
            Assert.Equal(entry.Id, result[0].Id);
            Assert.True(result[0].TechnicianMarkedVerified);

            await store.DeleteAsync(entry.Id);
            Assert.Empty(await store.SearchAsync("A546E"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsMissingFieldsAndEmptySafeSteps()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new RepairKnowledgeStore(root);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.AddAsync("Samsung", "SM-A546E", "No charge", "Diagnosis", "",
                    [], "manual", "Technician", false));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.AddAsync("", "SM-A546E", "No charge", "Diagnosis", "",
                    ["Inspect cable"], "manual", "Technician", false));
            Assert.Equal(0, await store.CountAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task UpdatesExistingKnowledgeAndPreservesItsIdentityAndCreationTime()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new RepairKnowledgeStore(root);
            var created = await store.AddAsync(
                "Samsung", "SM-A546E", "No charge", "Inspect charging path",
                "USB meter observation", ["Inspect cable"], "Service guide", "Tech A", false);

            var updated = await store.UpdateAsync(
                created.Id, "Samsung", "SM-A546E", "Slow charge", "Compare USB input and battery current",
                "Bench sample 2", ["Read battery status", "Compare reference limits"],
                "OEM service guide page 12", "Tech B", true);

            Assert.Equal(created.Id, updated.Id);
            Assert.Equal(created.CreatedAtUtc, updated.CreatedAtUtc);
            Assert.True(updated.UpdatedAtUtc >= created.UpdatedAtUtc);
            Assert.Equal("Slow charge", updated.Symptom);
            Assert.Equal("Tech B", updated.Author);
            Assert.True(updated.TechnicianMarkedVerified);
            var stored = Assert.Single(await store.SearchAsync("Slow charge"));
            Assert.Equal(updated.Id, stored.Id);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task UpdateRejectsAnUnknownEntryId()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new RepairKnowledgeStore(root);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => store.UpdateAsync(
                Guid.NewGuid(), "Samsung", "SM-A546E", "Symptom", "Diagnosis", "Evidence",
                ["Safe inspection"], "Manual", "Tech", false));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DoesNotStoreOrIndexDeviceSerialFields()
    {
        var root = CreateTempDirectory();
        try
        {
            var store = new RepairKnowledgeStore(root);
            await store.AddAsync("Xiaomi", "2312DRA50G", "Boot loop", "Technician reviewed boot log",
                "No customer/device serial stored",
                ["Read permitted logs", "Verify exact firmware product code"],
                "Official service instructions", "Workshop tech", false);
            var content = await File.ReadAllBytesAsync(store.DatabasePath);
            Assert.DoesNotContain("R58M123456A", System.Text.Encoding.UTF8.GetString(content));
            Assert.DoesNotContain("serial", string.Join("|", (await store.SearchAsync()).Select(x => x.Symptom)), StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "softbox-repair-knowledge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
