using System.Security.Cryptography;
using System.Text.Json;
using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SoftBoxSupportAuthorizationTests
{
    [Fact]
    public void OwnerPublicKeyIsPinnedAndMatchesTheExportedKeyFingerprint()
    {
        var root = CreateTempDirectory();
        try
        {
            var authorization = new SoftBoxSupportAuthorizationService(root);
            Assert.True(authorization.IsTrustedKeyConfigured);
            Assert.Equal(
                "9631EEA2D7DB1177D529DC487D18055ECF491CA019A27B3AF2AD9E06F4A4FCFE",
                authorization.TrustedPublicKeyFingerprint);
            Assert.Single(authorization.TrustedPublicKeyFingerprints);
        }
        finally { DeleteTempDirectory(root); }
    }

    [Fact]
    public async Task SecondaryOwnerKeyIsAcceptedWithoutReplacingTheExistingWindowsKey()
    {
        var root = CreateTempDirectory();
        using var windowsSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var androidSigner = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            var accounts = new SoftBoxAccountService(Path.Combine(root, "accounts"), 300_000);
            await accounts.CreateInitialAdministratorAsync("admin", "Administrator", "Old Administrator Password 12!");
            var windowsPem = windowsSigner.ExportSubjectPublicKeyInfoPem();
            var androidPem = androidSigner.ExportSubjectPublicKeyInfoPem();
            var auth = new SoftBoxSupportAuthorizationService(
                Path.Combine(root, "support"),
                trustedPublicKeyPems: new[] { windowsPem, androidPem });

            var request = ParseRequest(auth.CreatePasswordRecoveryRequest("admin"));
            Assert.False(string.IsNullOrWhiteSpace(request.DeviceLabel));
            var approval = CreateApprovalJson(androidSigner, request);
            var result = await auth.RecoverPasswordAsync(accounts, approval, "admin", "Recovered Android Owner Password 12!");

            Assert.Equal("admin", result.UserName);
            Assert.Equal(2, auth.TrustedPublicKeyFingerprints.Count);
        }
        finally { DeleteTempDirectory(root); }
    }

    [Fact]
    public async Task SignedPasswordRecoveryIsBoundToInstallationAndRequestAndCanBeUsedOnlyOnce()
    {
        var root = CreateTempDirectory();
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            var accountService = new SoftBoxAccountService(Path.Combine(root, "accounts"), 300_000);
            var admin = await accountService.CreateInitialAdministratorAsync("admin", "Administrator", "Old Administrator Password 12!");
            var authorization = new SoftBoxSupportAuthorizationService(Path.Combine(root, "support"), signer.ExportSubjectPublicKeyInfoPem());
            var request = ParseRequest(authorization.CreatePasswordRecoveryRequest("admin"));
            var approvalJson = CreateApprovalJson(signer, request);
            var recovered = await authorization.RecoverPasswordAsync(accountService, approvalJson, "admin", "New Administrator Password 12!");
            Assert.Equal(admin.Id, recovered.Id);
            Assert.Equal(admin.Id, (await accountService.AuthenticateAsync("admin", "New Administrator Password 12!")).Id);
            _ = authorization.CreatePasswordRecoveryRequest("admin");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                authorization.RecoverPasswordAsync(accountService, approvalJson, "admin", "Another Password 12!"));
        }
        finally { DeleteTempDirectory(root); }
    }

    [Fact]
    public async Task TamperedSignatureAndWrongActionAreRejectedWithoutChangingPassword()
    {
        var root = CreateTempDirectory();
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            var accounts = new SoftBoxAccountService(Path.Combine(root, "accounts"), 300_000);
            await accounts.CreateInitialAdministratorAsync("admin", "Administrator", "Old Administrator Password 12!");
            var auth = new SoftBoxSupportAuthorizationService(Path.Combine(root, "support"), signer.ExportSubjectPublicKeyInfoPem());
            var request = ParseRequest(auth.CreatePasswordRecoveryRequest("admin"));
            var approval = JsonSerializer.Deserialize<SoftBoxSupportApproval>(
                CreateApprovalJson(signer, request), JsonOptions())!;
            var forged = JsonSerializer.Serialize(approval with {
                Payload = approval.Payload with { Action = SoftBoxSupportAction.ResetLocalAccountDatabase }
            }, JsonOptions());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                auth.RecoverPasswordAsync(accounts, forged, "admin", "Attacker Password 12!"));

            var signatureTamper = JsonSerializer.Serialize(approval with {
                Payload = approval.Payload with { IssuedAtUtc = approval.Payload.IssuedAtUtc.AddSeconds(-30) }
            }, JsonOptions());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                auth.RecoverPasswordAsync(accounts, signatureTamper, "admin", "Another Attacker Password 12!"));
            Assert.NotEqual(Guid.Empty, (await accounts.AuthenticateAsync("admin", "Old Administrator Password 12!")).Id);
        }
        finally { DeleteTempDirectory(root); }
    }

    [Fact]
    public async Task DatabaseResetDeletesOnlyAccountStoreAndPreservesIdentityAndReplayLedger()
    {
        var root = CreateTempDirectory();
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            var accounts = new SoftBoxAccountService(Path.Combine(root, "accounts"), 300_000);
            await accounts.CreateInitialAdministratorAsync("admin", "Administrator", "Administrator Password 12!");
            var supportRoot = Path.Combine(root, "support");
            var auth = new SoftBoxSupportAuthorizationService(supportRoot, signer.ExportSubjectPublicKeyInfoPem());
            var installationId = auth.InstallationId;
            var request = ParseRequest(auth.CreateDatabaseResetRequest());
            var approvalJson = CreateApprovalJson(signer, request);

            await auth.ResetLocalAccountDatabaseAsync(accounts, approvalJson);
            Assert.False(await accounts.HasAccountsAsync());
            Assert.True(File.Exists(Path.Combine(supportRoot, "installation-id.dpapi")));
            Assert.True(File.Exists(Path.Combine(supportRoot, "consumed-grants.dpapi")));
            var reopened = new SoftBoxSupportAuthorizationService(supportRoot, signer.ExportSubjectPublicKeyInfoPem());
            Assert.Equal(installationId, reopened.InstallationId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                reopened.ResetLocalAccountDatabaseAsync(accounts, approvalJson));
        }
        finally { DeleteTempDirectory(root); }
    }

    [Fact]
    public async Task AnotherInstallationCannotConsumeTheApproval()
    {
        var root = CreateTempDirectory();
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            var firstAccounts = new SoftBoxAccountService(Path.Combine(root, "accounts-a"), 300_000);
            var secondAccounts = new SoftBoxAccountService(Path.Combine(root, "accounts-b"), 300_000);
            await firstAccounts.CreateInitialAdministratorAsync("admin", "First", "First Password 123!");
            await secondAccounts.CreateInitialAdministratorAsync("admin", "Second", "Second Password 123!");
            var first = new SoftBoxSupportAuthorizationService(Path.Combine(root, "support-a"), signer.ExportSubjectPublicKeyInfoPem());
            var second = new SoftBoxSupportAuthorizationService(Path.Combine(root, "support-b"), signer.ExportSubjectPublicKeyInfoPem());
            var request = ParseRequest(first.CreatePasswordRecoveryRequest("admin"));
            var approval = CreateApprovalJson(signer, request);
            _ = second.CreatePasswordRecoveryRequest("admin");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                second.RecoverPasswordAsync(secondAccounts, approval, "admin", "Recovered Password 123!"));
            Assert.NotEqual(first.InstallationId, second.InstallationId);
        }
        finally { DeleteTempDirectory(root); }
    }

    private static SoftBoxSupportRequest ParseRequest(string json)
        => JsonSerializer.Deserialize<SoftBoxSupportRequest>(json, JsonOptions())!;

    private static string CreateApprovalJson(ECDsa signer, SoftBoxSupportRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new SoftBoxSupportApprovalPayload(1, Guid.NewGuid(), request.RequestId,
            request.InstallationId, request.Nonce, request.Action, request.SubjectUserName, now, now.AddHours(1));
        var signature = signer.SignData(SoftBoxSupportAuthorizationService.GetCanonicalPayload(payload),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return JsonSerializer.Serialize(new SoftBoxSupportApproval(payload, Convert.ToBase64String(signature)), JsonOptions());
    }

    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SoftBox-Support-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
