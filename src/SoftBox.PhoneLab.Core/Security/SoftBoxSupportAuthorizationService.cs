using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SoftBox.PhoneLab.Core.Security;

public enum SoftBoxSupportAction { PasswordRecovery, ResetLocalAccountDatabase }

public sealed record SoftBoxSupportRequest(
    int Version, Guid RequestId, Guid InstallationId, string Nonce,
    SoftBoxSupportAction Action, string SubjectUserName, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);

public sealed record SoftBoxSupportApprovalPayload(
    int Version, Guid GrantId, Guid RequestId, Guid InstallationId, string Nonce,
    SoftBoxSupportAction Action, string SubjectUserName, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc);

public sealed record SoftBoxSupportApproval(SoftBoxSupportApprovalPayload Payload, string Signature);

/// <summary>
/// Verifies owner-signed, installation-bound support approvals. Installation identity and the
/// replay ledger live outside users.json so a local account reset cannot make an approval reusable.
/// </summary>
public sealed class SoftBoxSupportAuthorizationService
{
    private const int ProtocolVersion = 1;
    private const string PendingPurpose = "SoftBox.Support.PendingRequest.v1";
    private const string LedgerPurpose = "SoftBox.Support.ConsumedGrantLedger.v1";
    private const string InstallationPurpose = "SoftBox.Support.InstallationIdentity.v1";
    private const string PinnedPublicKeyPem = "SOFTBOX_OWNER_SUPPORT_PUBLIC_KEY_NOT_CONFIGURED";
    private static readonly TimeSpan RequestLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaximumGrantLifetime = TimeSpan.FromHours(24);
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _root;
    private readonly string _pendingPath;
    private readonly string _ledgerPath;
    private readonly string _lockPath;
    private readonly string _trustedPublicKeyPem;

    public Guid InstallationId { get; }
    public bool IsTrustedKeyConfigured =>
        !string.IsNullOrWhiteSpace(_trustedPublicKeyPem) &&
        !_trustedPublicKeyPem.Contains("NOT_CONFIGURED", StringComparison.Ordinal);

    public SoftBoxSupportAuthorizationService(string? rootDirectory = null, string? trustedPublicKeyPem = null)
    {
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "SupportAuthorization"));
        Directory.CreateDirectory(_root);
        _pendingPath = Path.Combine(_root, "pending-request.dpapi");
        _ledgerPath = Path.Combine(_root, "consumed-grants.dpapi");
        _lockPath = Path.Combine(_root, "authorization.lock");
        _trustedPublicKeyPem = trustedPublicKeyPem ?? PinnedPublicKeyPem;
        InstallationId = LoadOrCreateInstallationId();
    }

    public string CreatePasswordRecoveryRequest(string userName)
        => CreateRequest(SoftBoxSupportAction.PasswordRecovery, SoftBoxAccountService.NormalizeUserName(userName));

    public string CreateDatabaseResetRequest()
        => CreateRequest(SoftBoxSupportAction.ResetLocalAccountDatabase, string.Empty);

    private string CreateRequest(SoftBoxSupportAction action, string subjectUserName)
    {
        using var fileLock = AcquireLock();
        var now = DateTimeOffset.UtcNow;
        var request = new SoftBoxSupportRequest(
            ProtocolVersion, Guid.NewGuid(), InstallationId,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            action, subjectUserName, now, now.Add(RequestLifetime));
        WriteProtectedJson(_pendingPath, request, PendingPurpose);
        return JsonSerializer.Serialize(request, JsonOptions);
    }

    public async Task<AuthenticatedSoftBoxUser> RecoverPasswordAsync(
        SoftBoxAccountService accountService, string approvalJson, string userName, string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountService);
        SoftBoxAccountService.ValidatePassword(newPassword);
        var normalized = SoftBoxAccountService.NormalizeUserName(userName);
        var payload = ConsumeApproval(approvalJson, SoftBoxSupportAction.PasswordRecovery, normalized);
        return await accountService.RecoverPasswordWithSignedSupportGrantAsync(
            payload, normalized, newPassword, cancellationToken);
    }

    public async Task ResetLocalAccountDatabaseAsync(
        SoftBoxAccountService accountService, string approvalJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountService);
        var payload = ConsumeApproval(approvalJson, SoftBoxSupportAction.ResetLocalAccountDatabase, string.Empty);
        await accountService.ResetLocalAccountDatabaseWithSignedGrantAsync(payload, cancellationToken);
        AppendSupportAudit(payload, "LocalAccountDatabaseReset");
    }

    private SoftBoxSupportApprovalPayload ConsumeApproval(
        string approvalJson, SoftBoxSupportAction expectedAction, string expectedUserName)
    {
        if (!IsTrustedKeyConfigured)
            throw new InvalidOperationException(
                "مفتاح دعم SoftBox العام لم يتم تثبيته داخل هذا الإصدار بعد. صدّر المفتاح العام من أداة المالك ثم ثبّته في إعدادات التحقق قبل إصدار نسخة العملاء.");
        if (string.IsNullOrWhiteSpace(approvalJson) || approvalJson.Length > 65_536)
            throw new InvalidDataException("ملف موافقة الدعم فارغ أو تجاوز الحجم المسموح.");

        using var fileLock = AcquireLock();
        var pending = ReadProtectedJson<SoftBoxSupportRequest>(_pendingPath, PendingPurpose)
            ?? throw new InvalidOperationException("لا يوجد طلب دعم معلّق على هذا التثبيت. أنشئ طلبًا جديدًا أولًا.");

        SoftBoxSupportApproval approval;
        try
        {
            approval = JsonSerializer.Deserialize<SoftBoxSupportApproval>(approvalJson, JsonOptions)
                ?? throw new InvalidDataException("ملف موافقة الدعم غير صالح.");
        }
        catch (JsonException ex) { throw new InvalidDataException("تعذر قراءة ملف موافقة الدعم.", ex); }

        var payload = approval.Payload ?? throw new InvalidDataException("محتوى الموافقة غير موجود.");
        var now = DateTimeOffset.UtcNow;
        if (payload.Version != ProtocolVersion || payload.GrantId == Guid.Empty ||
            payload.RequestId != pending.RequestId || payload.InstallationId != InstallationId ||
            payload.InstallationId != pending.InstallationId ||
            !string.Equals(payload.Nonce, pending.Nonce, StringComparison.Ordinal) ||
            payload.Action != expectedAction || payload.Action != pending.Action ||
            !string.Equals(payload.SubjectUserName ?? string.Empty, expectedUserName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(payload.SubjectUserName ?? string.Empty, pending.SubjectUserName, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("الموافقة لا تطابق طلب هذا الجهاز أو المستخدم أو نوع الإجراء.");

        if (pending.ExpiresAtUtc <= now || payload.IssuedAtUtc > now.AddMinutes(5) ||
            payload.ExpiresAtUtc <= now || payload.ExpiresAtUtc <= payload.IssuedAtUtc ||
            payload.ExpiresAtUtc - payload.IssuedAtUtc > MaximumGrantLifetime ||
            payload.ExpiresAtUtc > pending.ExpiresAtUtc)
            throw new UnauthorizedAccessException("الموافقة أو الطلب منتهٍ أو أن مدة الصلاحية غير مقبولة.");

        using var publicKey = ECDsa.Create();
        try { publicKey.ImportFromPem(_trustedPublicKeyPem); }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        { throw new CryptographicException("مفتاح التحقق العام المثبّت داخل SoftBox غير صالح.", ex); }
        if (!VerifySignature(approval, publicKey))
            throw new UnauthorizedAccessException("التوقيع الرقمي للموافقة غير صحيح أو تم تعديل محتواها.");

        var ledger = ReadProtectedJson<ConsumedGrantLedger>(_ledgerPath, LedgerPurpose) ?? new ConsumedGrantLedger();
        ledger.GrantIds ??= [];
        ledger.RequestIds ??= [];
        var grantId = payload.GrantId.ToString("D");
        var requestId = payload.RequestId.ToString("D");
        if (ledger.GrantIds.Contains(grantId, StringComparer.OrdinalIgnoreCase) ||
            ledger.RequestIds.Contains(requestId, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("تم استخدام هذه الموافقة أو طلب الدعم من قبل. لا يمكن إعادة استخدامها.");
        if (ledger.GrantIds.Count >= 100_000 || ledger.RequestIds.Count >= 100_000)
            throw new InvalidOperationException("وصل سجل الموافقات المستهلكة إلى الحد الآمن. تم إيقاف الاسترداد دون حذف السجل.");

        ledger.GrantIds.Add(grantId);
        ledger.RequestIds.Add(requestId);
        WriteProtectedJson(_ledgerPath, ledger, LedgerPurpose);
        File.Delete(_pendingPath);
        return payload;
    }

    private Guid LoadOrCreateInstallationId()
    {
        var path = Path.Combine(_root, "installation-id.dpapi");
        if (File.Exists(path))
        {
            var protectedBytes = File.ReadAllBytes(path);
            byte[] plain;
            bool wasProtected;
            try { plain = WindowsUserDataProtection.UnprotectOrLegacy(protectedBytes, InstallationPurpose, out wasProtected); }
            finally { CryptographicOperations.ZeroMemory(protectedBytes); }
            try
            {
                var text = Encoding.UTF8.GetString(plain);
                if (!Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty)
                    throw new InvalidDataException("معرّف تثبيت SoftBox المخزن غير صالح. لم يتم إنشاء معرّف بديل كي لا يتغير ربط الموافقات.");
                if (!wasProtected) WriteProtectedBytes(path, plain, InstallationPurpose);
                return id;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }

        var created = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes(created.ToString("D"));
        try { WriteProtectedBytes(path, bytes, InstallationPurpose); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        return created;
    }

    private FileStream AcquireLock()
    {
        try { return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex)
        { throw new InvalidOperationException("توجد عملية دعم أخرى تعمل على هذا التثبيت. أغلقها ثم أعد المحاولة.", ex); }
    }

    private static bool VerifySignature(SoftBoxSupportApproval approval, ECDsa publicKey)
    {
        byte[] signature;
        try { signature = Convert.FromBase64String(approval.Signature); }
        catch (FormatException) { return false; }
        var canonical = GetCanonicalPayload(approval.Payload);
        try
        {
            return publicKey.VerifyData(canonical, signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        finally { CryptographicOperations.ZeroMemory(signature); CryptographicOperations.ZeroMemory(canonical); }
    }

    public static byte[] GetCanonicalPayload(SoftBoxSupportApprovalPayload payload)
    {
        var fields = new[]
        {
            payload.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            payload.GrantId.ToString("D"), payload.RequestId.ToString("D"),
            payload.InstallationId.ToString("D"), payload.Nonce, payload.Action.ToString(),
            payload.SubjectUserName ?? string.Empty,
            payload.IssuedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            payload.ExpiresAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        };
        return Encoding.UTF8.GetBytes(string.Join("\n", fields));
    }

    private static System.Text.Json.JsonSerializerOptions CreateJsonOptions()
        => new(System.Text.Json.JsonSerializerDefaults.Web)
        { WriteIndented = true, PropertyNameCaseInsensitive = false,
          Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static T? ReadProtectedJson<T>(string path, string purpose)
    {
        if (!File.Exists(path)) return default;
        var stored = File.ReadAllBytes(path);
        byte[] plain;
        bool wasProtected;
        try { plain = WindowsUserDataProtection.UnprotectOrLegacy(stored, purpose, out wasProtected); }
        finally { CryptographicOperations.ZeroMemory(stored); }
        try
        {
            if (!wasProtected) throw new CryptographicException("سجل تفويض SoftBox ليس محميًا بـ DPAPI. تم رفضه.");
            return JsonSerializer.Deserialize<T>(plain, JsonOptions);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static void WriteProtectedJson<T>(string path, T value, string purpose)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try { WriteProtectedBytes(path, bytes, purpose); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void WriteProtectedBytes(string path, byte[] bytes, string purpose)
    {
        var protectedBytes = WindowsUserDataProtection.Protect(bytes, purpose);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(protectedBytes); stream.Flush(flushToDisk: true); }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private void AppendSupportAudit(SoftBoxSupportApprovalPayload payload, string eventType)
    {
        var record = new
        {
            atUtc = DateTimeOffset.UtcNow, eventType, payload.GrantId, payload.RequestId,
            payload.InstallationId, action = payload.Action.ToString()
        };
        File.AppendAllText(Path.Combine(_root, "support-audit.jsonl"),
            JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
    }

    private sealed class ConsumedGrantLedger
    {
        public List<string> GrantIds { get; set; } = [];
        public List<string> RequestIds { get; set; } = [];
    }
}
