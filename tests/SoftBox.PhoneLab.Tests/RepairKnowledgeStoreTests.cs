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
