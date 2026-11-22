using System.Security.Cryptography;
using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SoftBoxAccountServiceTests
{
    private static SoftBoxAccountService CreateService(string directory)
        => new(directory, passwordIterations: 300_000);

    [Fact]
    public async Task FirstAdministratorIsCreatedAndPasswordIsNeverStoredAsPlainText()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            Assert.False(await service.HasAccountsAsync());

            var user = await service.CreateInitialAdministratorAsync(
                "Admin", "مدير النظام", "Correct Horse Battery 42!");

            Assert.Equal(SoftBoxUserRole.Administrator, user.Role);
            Assert.True(await service.HasAccountsAsync());

            var stored = await File.ReadAllBytesAsync(service.StorePath);
            Assert.True(WindowsUserDataProtection.IsProtected(stored));
            var jsonBytes = WindowsUserDataProtection.UnprotectOrLegacy(
                stored, "SoftBoxAccountStore.v1", out var wasProtected);
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
                Assert.True(wasProtected);
                Assert.DoesNotContain("Correct Horse Battery 42!", json);
                Assert.Contains("PasswordHash", json);
                Assert.Contains("PasswordSalt", json);
            }
            finally { CryptographicOperations.ZeroMemory(jsonBytes); }

            var login = await service.AuthenticateAsync("admin", "Correct Horse Battery 42!");
            Assert.Equal(user.Id, login.Id);
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task LegacyPlaintextAccountStoreMigratesToDpapiWithoutLosingLogin()
    {
        var directory = CreateTempDirectory();
        try
        {
            var initial = CreateService(directory);
            var admin = await initial.CreateInitialAdministratorAsync(
                "admin", "Legacy Administrator", "Legacy Administrator Password 12!");

            var path = initial.StorePath;
            var protectedBytes = await File.ReadAllBytesAsync(path);
            var legacyJson = WindowsUserDataProtection.UnprotectOrLegacy(
                protectedBytes, "SoftBoxAccountStore.v1", out var wasProtected);
            Assert.True(wasProtected);
            await File.WriteAllBytesAsync(path, legacyJson);
            CryptographicOperations.ZeroMemory(legacyJson);

            var migrated = CreateService(directory);
            Assert.True(await migrated.HasAccountsAsync());
            Assert.True(WindowsUserDataProtection.IsProtected(await File.ReadAllBytesAsync(path)));
            Assert.Equal(admin.Id, (await migrated.AuthenticateAsync(
                "admin", "Legacy Administrator Password 12!")).Id);
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task InitialAdministratorCannotBeCreatedTwice()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            await service.CreateInitialAdministratorAsync("admin", "مدير النظام", "Initial Password 123!");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateInitialAdministratorAsync("other", "مدير آخر", "Another Password 123!"));
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task AdministratorCanCreateTechnicianAndReadOnlyAccounts()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var admin = await service.CreateInitialAdministratorAsync("admin", "المدير", "Administrator Password 12!");
            var tech = await service.CreateUserAsync(admin.Id, "tech01", "فني صيانة", SoftBoxUserRole.Technician, "Technician Password 12!");
            var viewer = await service.CreateUserAsync(admin.Id, "viewer01", "مشاهد التقارير", SoftBoxUserRole.ReadOnly, "Read Only Password 12!");

            Assert.Equal(SoftBoxUserRole.Technician, tech.Role);
            Assert.Equal(SoftBoxUserRole.ReadOnly, viewer.Role);

            var users = await service.GetUsersAsync(admin.Id);
            Assert.Equal(3, users.Count);
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task NonAdministratorCannotListOrCreateAccounts()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var admin = await service.CreateInitialAdministratorAsync("admin", "المدير", "Administrator Password 12!");
            var tech = await service.CreateUserAsync(admin.Id, "tech01", "فني", SoftBoxUserRole.Technician, "Technician Password 12!");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetUsersAsync(tech.Id));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.CreateUserAsync(tech.Id, "other01", "مستخدم", SoftBoxUserRole.ReadOnly, "Another Password 12!"));
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task FailedLoginsTriggerTemporaryLockout()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var firstAdmin = await service.CreateInitialAdministratorAsync("admin", "المدير", "Administrator Password 12!");
            var observer = await service.CreateUserAsync(firstAdmin.Id, "observer", "مدير احتياطي", SoftBoxUserRole.Administrator, "Observer Administrator 12!");
            for (var index = 0; index < 5; index++)
                await Assert.ThrowsAsync<SoftBoxAuthenticationException>(() => service.AuthenticateAsync("admin", "Wrong Password 123!"));

            var summary = Assert.Single(await service.GetUsersAsync(observer.Id), x => x.Id == firstAdmin.Id);
            Assert.NotNull(summary.LockedUntilUtc);
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task LastActiveAdministratorCannotBeDisabledOrDemoted()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var admin = await service.CreateInitialAdministratorAsync("admin", "المدير", "Administrator Password 12!");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateUserAsync(admin.Id, admin.Id, "المدير", SoftBoxUserRole.Technician, true));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateUserAsync(admin.Id, admin.Id, "المدير", SoftBoxUserRole.Administrator, false));
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task AdministratorCanResetAnotherUsersPasswordAndUnlockAccount()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var admin = await service.CreateInitialAdministratorAsync("admin", "المدير", "Administrator Password 12!");
            var tech = await service.CreateUserAsync(admin.Id, "tech01", "فني", SoftBoxUserRole.Technician, "Old Technician Password!");
            await service.ResetUserPasswordAsync(admin.Id, tech.Id, "New Technician Password 12!");

            var login = await service.AuthenticateAsync("tech01", "New Technician Password 12!");
            Assert.Equal(tech.Id, login.Id);
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task UserCanChangeOwnPasswordAndOldPasswordStopsWorking()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var user = await service.CreateInitialAdministratorAsync("admin", "المدير", "Old Administrator Password!");
            await service.ChangeOwnPasswordAsync(user.Id, "Old Administrator Password!", "New Administrator Password 12!");

            await Assert.ThrowsAsync<SoftBoxAuthenticationException>(() =>
                service.AuthenticateAsync("admin", "Old Administrator Password!"));
            Assert.Equal(user.Id, (await service.AuthenticateAsync("admin", "New Administrator Password 12!")).Id);
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task DisablingAnotherUserPreventsFutureLoginButPreservesAccount()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var admin = await service.CreateInitialAdministratorAsync("admin", "المدير", "Administrator Password 12!");
            var tech = await service.CreateUserAsync(admin.Id, "tech01", "فني", SoftBoxUserRole.Technician, "Technician Password 12!");
            await service.UpdateUserAsync(admin.Id, tech.Id, "فني صيانة", SoftBoxUserRole.Technician, false);

            await Assert.ThrowsAsync<SoftBoxAuthenticationException>(() =>
                service.AuthenticateAsync("tech01", "Technician Password 12!"));
            Assert.Contains(await service.GetUsersAsync(admin.Id), x => x.Id == tech.Id && !x.IsEnabled);
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task ToolInvocationIsRecordedAgainstTheAuthenticatedUser()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var admin = await service.CreateInitialAdministratorAsync("admin", "المدير", "Administrator Password 12!");
            var tech = await service.CreateUserAsync(admin.Id, "tech01", "فني", SoftBoxUserRole.Technician, "Technician Password 12!");
            await service.RecordActionInvokedAsync(tech.Id, "OpenAndroidFileManager");

            var audit = await service.GetAuditAsync(admin.Id);
            var action = Assert.Single(audit, x => x.EventType == "ModuleActionInvoked");
            Assert.Equal(tech.Id, action.ActorUserId);
            Assert.Equal(tech.Id, action.SubjectUserId);
            Assert.Equal("OpenAndroidFileManager", action.Reference);
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task AuditCapturesUserActionsButNeverPasswords()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var password = "Administrator Password 12!";
            var admin = await service.CreateInitialAdministratorAsync("admin", "المدير", password);
            await service.RecordSessionLockedAsync(admin.Id);
            await service.RecordSessionUnlockedAsync(admin.Id);
            await service.RecordAccessDeniedAsync(admin.Id, "OpenPluginStore");

            var audit = await service.GetAuditAsync(admin.Id);
            Assert.Contains(audit, x => x.EventType == "SessionLocked");
            Assert.Contains(audit, x => x.EventType == "SessionUnlocked");
            Assert.Contains(audit, x => x.EventType == "AccessDenied");
            Assert.DoesNotContain(string.Join("\n", audit.Select(x => x.Description)), password);
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task CorruptedAccountStoreFailsClosedInsteadOfResettingToFirstRun()
    {
        var directory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "users.json"), "{ invalid");
            var service = CreateService(directory);
            await Assert.ThrowsAnyAsync<Exception>(() => service.HasAccountsAsync());
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task OneThousandInstallationRecoveryCodesAreHashedAndConsumedOnce()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var admin = await service.CreateInitialAdministratorAsync(
                "admin", "المدير", "Administrator Password 12!");
            var codes = await service.GenerateSupportRecoveryCodesAsync(admin.Id, 1_000);

            Assert.Equal(1_000, codes.Count);
            Assert.Equal(1_000, codes.Distinct(StringComparer.Ordinal).Count());
            Assert.All(codes, code => Assert.Matches("^[A-F0-9]{32}$", code));

            var stored = await File.ReadAllBytesAsync(service.StorePath);
            var jsonBytes = WindowsUserDataProtection.UnprotectOrLegacy(
                stored, "SoftBoxAccountStore.v1", out var protectedStore);
            try
            {
                Assert.True(protectedStore);
                var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
                Assert.DoesNotContain(codes[0], json);
                Assert.Contains("RecoveryCodePepper", json);
                Assert.Contains("CodeHash", json);
            }
            finally { CryptographicOperations.ZeroMemory(jsonBytes); }

            var newPassword = "Recovered Administrator Password 12!";
            var recovered = await service.RecoverPasswordWithSupportCodeAsync(
                "admin", codes[0], newPassword);
            Assert.Equal(admin.Id, recovered.Id);
            Assert.Equal(admin.Id, (await service.AuthenticateAsync("admin", newPassword)).Id);

            await Assert.ThrowsAsync<SoftBoxAuthenticationException>(() =>
                service.RecoverPasswordWithSupportCodeAsync(
                    "admin", codes[0], "Another Recovered Password 12!"));
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task OnlyAnAdministratorCanIssueInstallationRecoveryCodes()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = CreateService(directory);
            var admin = await service.CreateInitialAdministratorAsync(
                "admin", "المدير", "Administrator Password 12!");
            var technician = await service.CreateUserAsync(
                admin.Id, "tech01", "فني", SoftBoxUserRole.Technician, "Technician Password 12!");

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.GenerateSupportRecoveryCodesAsync(technician.Id, 1));
        }
        finally { DeleteTempDirectory(directory); }
    }

    [Fact]
    public async Task SupportRecoveryCodesCannotBeUsedOnAnotherInstallation()
    {
        var firstDirectory = CreateTempDirectory();
        var secondDirectory = CreateTempDirectory();
        try
        {
            var first = CreateService(firstDirectory);
            var second = CreateService(secondDirectory);
            var firstAdmin = await first.CreateInitialAdministratorAsync(
                "admin", "المدير الأول", "First Administrator Password 12!");
            await second.CreateInitialAdministratorAsync(
                "admin", "المدير الثاني", "Second Administrator Password 12!");
            var code = Assert.Single(await first.GenerateSupportRecoveryCodesAsync(firstAdmin.Id, 1));

            await Assert.ThrowsAsync<SoftBoxAuthenticationException>(() =>
                second.RecoverPasswordWithSupportCodeAsync(
                    "admin", code, "Recovered Password For Second 12!"));
        }
        finally
        {
            DeleteTempDirectory(firstDirectory);
            DeleteTempDirectory(secondDirectory);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SoftBox-Accounts-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
