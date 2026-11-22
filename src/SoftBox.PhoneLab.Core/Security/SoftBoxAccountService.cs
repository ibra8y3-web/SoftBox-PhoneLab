using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Security;

public enum SoftBoxUserRole
{
    Administrator,
    Technician,
    ReadOnly
}

[Flags]
public enum SoftBoxAccountPermission
{
    None = 0,
    ViewWorkspace = 1,
    DeviceRead = 2,
    DeviceOperations = 4,
    WorkshopOperations = 8,
    ExportData = 16,
    ManageUsers = 32,
    ManageSettings = 64,
    ManagePlugins = 128,
    ManageUpdates = 256,
    LockSession = 512
}

public sealed record AuthenticatedSoftBoxUser(
    Guid Id,
    string UserName,
    string DisplayName,
    SoftBoxUserRole Role,
    DateTimeOffset AuthenticatedAtUtc);

public sealed record SoftBoxUserSummary(
    Guid Id,
    string UserName,
    string DisplayName,
    SoftBoxUserRole Role,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset? LockedUntilUtc,
    int FailedLoginAttempts);

public sealed record SoftBoxUserAuditEntry(
    Guid Id,
    DateTimeOffset AtUtc,
    Guid? ActorUserId,
    Guid? SubjectUserId,
    string EventType,
    string Reference,
    string Description);

public sealed class SoftBoxAuthenticationException(string message) : Exception(message);

/// <summary>
/// Offline local account store. Passwords are never persisted: only per-user random salts
/// and PBKDF2-HMAC-SHA256 verifiers are saved. All changes use atomic file replacement.
/// </summary>
public sealed class SoftBoxAccountService
{
    public const int DefaultPasswordIterations = 600_000;
    public const int MinimumPasswordLength = 12;
    private const int PasswordBytes = 32;
    private const int SaltBytes = 16;
    private const int MaximumAccounts = 100;
    private const int MaximumAuditEntries = 5_000;
    private const int MaximumStoreBytes = 8 * 1024 * 1024;
    private const int MaximumSupportRecoveryCodes = 10_000;
    private const int MaximumFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly Regex UserNamePattern = new("^[a-zA-Z0-9][a-zA-Z0-9._-]{2,31}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _storePath;
    private readonly int _passwordIterations;

    public string StorePath => _storePath;
    public int PasswordIterations => _passwordIterations;

    public SoftBoxAccountService(string? rootDirectory = null, int passwordIterations = DefaultPasswordIterations)
    {
        if (passwordIterations is < 300_000 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(passwordIterations), "PBKDF2 iterations must be between 300,000 and 1,000,000.");

        _passwordIterations = passwordIterations;
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftBox", "PhoneLab", "Security");
        Directory.CreateDirectory(root);
        _storePath = Path.Combine(Path.GetFullPath(root), "users.json");
    }

    public async Task<bool> HasAccountsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            return data.Accounts.Count > 0;
        }
        finally { _gate.Release(); }
    }

    internal async Task EnsureAccountCanRecoverWithSignedSupportGrantAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeUserName(userName);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            var account = data.Accounts.FirstOrDefault(x =>
                x.UserName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (account is null || !account.IsEnabled)
                throw new SoftBoxAuthenticationException("الحساب المطلوب غير موجود أو معطل. لم يتم استهلاك موافقة الدعم.");
        }
        finally { _gate.Release(); }
    }

    public async Task<AuthenticatedSoftBoxUser> CreateInitialAdministratorAsync(
        string userName,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeUserName(userName);
        ValidateDisplayName(displayName);
        ValidatePassword(password);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            if (data.Accounts.Count != 0)
                throw new InvalidOperationException("تم إنشاء حساب بالفعل. لا يمكن تكرار إعداد المدير الأول.");

            var account = CreateAccount(normalized, displayName, SoftBoxUserRole.Administrator, password);
            data.Accounts.Add(account);
            AddAudit(data, null, account.Id, "InitialAdministratorCreated", normalized,
                "تم إنشاء حساب المدير الأول على هذا الجهاز.");
            await SaveLockedAsync(data, cancellationToken);
            return ToAuthenticatedUser(account);
        }
        finally { _gate.Release(); }
    }

    public async Task<AuthenticatedSoftBoxUser> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (password is null)
            throw new ArgumentNullException(nameof(password));

        var normalized = NormalizeUserNameForLookup(userName);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            var account = data.Accounts.FirstOrDefault(x =>
                x.UserName.Equals(normalized, StringComparison.OrdinalIgnoreCase));

            if (account is null)
            {
                PerformDummyPasswordWork(password);
                AddAudit(data, null, null, "LoginFailed", normalized, "محاولة تسجيل دخول مرفوضة.");
                await SaveLockedAsync(data, cancellationToken);
                throw new SoftBoxAuthenticationException("اسم المستخدم أو كلمة المرور غير صحيحة.");
            }

            var now = DateTimeOffset.UtcNow;
            if (account.LockedUntilUtc is DateTimeOffset lockedUntil && lockedUntil > now)
            {
                AddAudit(data, account.Id, account.Id, "LoginBlockedByLockout", account.UserName,
                    "محاولة دخول أثناء فترة القفل المؤقت.");
                await SaveLockedAsync(data, cancellationToken);
                var remaining = Math.Max(1, (int)Math.Ceiling((lockedUntil - now).TotalMinutes));
                throw new SoftBoxAuthenticationException($"الحساب مقفل مؤقتًا. حاول بعد {remaining} دقيقة تقريبًا.");
            }

            if (account.LockedUntilUtc is not null)
            {
                account.LockedUntilUtc = null;
                account.FailedLoginAttempts = 0;
            }

            var validPassword = VerifyPassword(password, account.PasswordSalt, account.PasswordHash, account.PasswordIterations);
            if (!account.IsEnabled || !validPassword)
            {
                account.FailedLoginAttempts = Math.Min(MaximumFailedAttempts, account.FailedLoginAttempts + 1);
                if (account.FailedLoginAttempts >= MaximumFailedAttempts)
                {
                    account.LockedUntilUtc = now.Add(LockoutDuration);
                    AddAudit(data, account.Id, account.Id, "AccountTemporarilyLocked", account.UserName,
                        "تم قفل الحساب مؤقتًا بعد محاولات مصادقة فاشلة متكررة.");
                }
                else
                {
                    AddAudit(data, account.Id, account.Id, "LoginFailed", account.UserName,
                        account.IsEnabled ? "فشل التحقق من كلمة المرور." : "محاولة تسجيل دخول لحساب معطل.");
                }

                account.UpdatedAtUtc = now;
                await SaveLockedAsync(data, cancellationToken);
                throw new SoftBoxAuthenticationException("اسم المستخدم أو كلمة المرور غير صحيحة.");
            }

            account.FailedLoginAttempts = 0;
            account.LockedUntilUtc = null;
            account.LastLoginAtUtc = now;
            account.UpdatedAtUtc = now;
            AddAudit(data, account.Id, account.Id, "LoginSucceeded", account.UserName, "تم تسجيل الدخول بنجاح.");
            await SaveLockedAsync(data, cancellationToken);
            return ToAuthenticatedUser(account, now);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<SoftBoxUserSummary>> GetUsersAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            RequireAdministrator(data, actorUserId);
            return data.Accounts
                .OrderBy(x => x.UserName, StringComparer.OrdinalIgnoreCase)
                .Select(ToSummary)
                .ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<SoftBoxUserSummary> CreateUserAsync(
        Guid actorUserId,
        string userName,
        string displayName,
        SoftBoxUserRole role,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeUserName(userName);
        ValidateDisplayName(displayName);
        ValidatePassword(password);
        ValidateRole(role);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            var actor = RequireAdministrator(data, actorUserId);
            if (data.Accounts.Count >= MaximumAccounts)
                throw new InvalidOperationException($"الحد الأقصى للحسابات المحلية هو {MaximumAccounts}.");
            if (data.Accounts.Any(x => x.UserName.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("اسم المستخدم مستخدم بالفعل.");

            var account = CreateAccount(normalized, displayName, role, password);
            data.Accounts.Add(account);
            AddAudit(data, actor.Id, account.Id, "UserCreated", normalized,
                $"أنشأ المدير حسابًا بدور {RoleLabel(role)}.");
            await SaveLockedAsync(data, cancellationToken);
            return ToSummary(account);
        }
        finally { _gate.Release(); }
    }

    public async Task<SoftBoxUserSummary> UpdateUserAsync(
        Guid actorUserId,
        Guid targetUserId,
        string displayName,
        SoftBoxUserRole role,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        ValidateDisplayName(displayName);
        ValidateRole(role);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            var actor = RequireAdministrator(data, actorUserId);
            var target = data.Accounts.FirstOrDefault(x => x.Id == targetUserId)
                ?? throw new KeyNotFoundException("الحساب المطلوب غير موجود.");

            if (target.Id == actor.Id && (!isEnabled || role != SoftBoxUserRole.Administrator))
                throw new InvalidOperationException("لا يمكن للمدير تعطيل حسابه الحالي أو تخفيض صلاحية حساب الجلسة النشطة.");

            var removesActiveAdmin = target.IsEnabled &&
                target.Role == SoftBoxUserRole.Administrator &&
                (!isEnabled || role != SoftBoxUserRole.Administrator);
            if (removesActiveAdmin && data.Accounts.Count(x =>
                    x.IsEnabled && x.Role == SoftBoxUserRole.Administrator && x.Id != target.Id) == 0)
                throw new InvalidOperationException("لا يمكن تعطيل أو تخفيض صلاحية آخر مدير نشط.");

            target.DisplayName = displayName.Trim();
            target.Role = role;
            target.IsEnabled = isEnabled;
            target.FailedLoginAttempts = 0;
            target.LockedUntilUtc = null;
            target.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(data, actor.Id, target.Id, "UserUpdated", target.UserName,
                $"تم تحديث بيانات الحساب والدور والحالة إلى {(isEnabled ? "نشط" : "معطل")}.");
            await SaveLockedAsync(data, cancellationToken);
            return ToSummary(target);
        }
        finally { _gate.Release(); }
    }

    public async Task ResetUserPasswordAsync(
        Guid actorUserId,
        Guid targetUserId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(newPassword);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            var actor = RequireAdministrator(data, actorUserId);
            if (actor.Id == targetUserId)
                throw new InvalidOperationException("استخدم تغيير كلمة المرور الخاص بحسابك بدل إعادة تعيين حساب المدير.");
            var target = data.Accounts.FirstOrDefault(x => x.Id == targetUserId)
                ?? throw new KeyNotFoundException("الحساب المطلوب غير موجود.");

            SetPassword(target, newPassword);
            target.FailedLoginAttempts = 0;
            target.LockedUntilUtc = null;
            target.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(data, actor.Id, target.Id, "PasswordResetByAdministrator", target.UserName,
                "أعاد المدير تعيين كلمة مرور الحساب. لا يتم تسجيل كلمة المرور في سجل التدقيق.");
            await SaveLockedAsync(data, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Issues one-time support recovery codes for this local installation. Plaintext codes are
    /// returned once to the administrator; only keyed verifiers are persisted in the DPAPI store.
    /// </summary>
    public async Task<IReadOnlyList<string>> GenerateSupportRecoveryCodesAsync(
        Guid actorUserId,
        int count = 1_000,
        CancellationToken cancellationToken = default)
    {
        if (count is < 1 or > 2_000)
            throw new ArgumentOutOfRangeException(nameof(count), "يمكن إصدار من 1 إلى 2000 رمز في الدفعة الواحدة.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            var actor = RequireAdministrator(data, actorUserId);
            data.RecoveryCodes ??= [];
            if (data.RecoveryCodes.Count + count > MaximumSupportRecoveryCodes)
                throw new InvalidOperationException("وصل مخزون رموز الدعم لهذا التثبيت إلى الحد الآمن. لا يمكن حذف سجل الرموز المستهلكة.");

            var pepper = Convert.FromBase64String(data.RecoveryCodePepper!);
            var issued = new List<string>(count);
            try
            {
                for (var index = 0; index < count; index++)
                {
                    var random = RandomNumberGenerator.GetBytes(16);
                    string code;
                    try { code = Convert.ToHexString(random); }
                    finally { CryptographicOperations.ZeroMemory(random); }

                    var codeBytes = Encoding.UTF8.GetBytes(code);
                    byte[] digest;
                    try { digest = HMACSHA256.HashData(pepper, codeBytes); }
                    finally { CryptographicOperations.ZeroMemory(codeBytes); }

                    data.RecoveryCodes.Add(new StoredSupportRecoveryCode
                    {
                        Id = Guid.NewGuid(),
                        CodeHash = Convert.ToBase64String(digest),
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    });
                    CryptographicOperations.ZeroMemory(digest);
                    issued.Add(code);
                }
            }
            finally { CryptographicOperations.ZeroMemory(pepper); }

            AddAudit(data, actor.Id, actor.Id, "SupportRecoveryCodesIssued", count.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
                $"أصدر المدير {count} رمز استرداد أحادي الاستخدام لهذا التثبيت.");
            await SaveLockedAsync(data, cancellationToken);
            return issued;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Applies a password recovery only after the support authorization service has verified
    /// the signed owner approval and atomically consumed its grant and request identifiers.
    /// Internal visibility prevents the desktop UI from bypassing signature verification.
    /// </summary>
    internal async Task<AuthenticatedSoftBoxUser> RecoverPasswordWithSignedSupportGrantAsync(
        SoftBoxSupportApprovalPayload approval,
        string userName,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (approval.Action != SoftBoxSupportAction.PasswordRecovery ||
            approval.GrantId == Guid.Empty || approval.RequestId == Guid.Empty ||
            approval.InstallationId == Guid.Empty)
            throw new UnauthorizedAccessException("موافقة استرداد كلمة المرور غير صالحة.");
        var normalized = NormalizeUserName(userName);
        if (!normalized.Equals(approval.SubjectUserName, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("الموافقة صادرة لمستخدم مختلف.");
        ValidatePassword(newPassword);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            var account = data.Accounts.FirstOrDefault(x =>
                x.UserName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (account is null || !account.IsEnabled)
                throw new SoftBoxAuthenticationException("الحساب المطلوب غير موجود أو معطل.");
            var now = DateTimeOffset.UtcNow;
            SetPassword(account, newPassword);
            account.FailedLoginAttempts = 0;
            account.LockedUntilUtc = null;
            account.LastLoginAtUtc = now;
            account.UpdatedAtUtc = now;
            AddAudit(data, null, account.Id, "PasswordRecoveredWithSignedSupportApproval", account.UserName,
                "أعيد تعيين كلمة مرور الحساب بموافقة دعم موقعة ومقيدة بهذا التثبيت والطلب.");
            await SaveLockedAsync(data, cancellationToken);
            return ToAuthenticatedUser(account, now);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Deletes only users.json after the authorization service has consumed a valid signed
    /// ResetLocalAccountDatabase grant. Support identity and replay state live outside this file.
    /// </summary>
    internal async Task ResetLocalAccountDatabaseWithSignedGrantAsync(
        SoftBoxSupportApprovalPayload approval,
        CancellationToken cancellationToken = default)
    {
        if (approval.Action != SoftBoxSupportAction.ResetLocalAccountDatabase ||
            approval.GrantId == Guid.Empty || approval.RequestId == Guid.Empty ||
            approval.InstallationId == Guid.Empty || !string.IsNullOrEmpty(approval.SubjectUserName))
            throw new UnauthorizedAccessException("موافقة تصفير قاعدة الحسابات غير صالحة.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_storePath))
                File.Delete(_storePath);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Resets only the named local account password when a valid, unused installation-bound
    /// support recovery code is supplied. Every accepted code is consumed atomically.
    /// </summary>
    public async Task<AuthenticatedSoftBoxUser> RecoverPasswordWithSupportCodeAsync(
        string userName,
        string recoveryCode,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(newPassword);
        var normalizedUserName = NormalizeUserNameForLookup(userName);
        var normalizedCode = (recoveryCode ?? string.Empty)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
        if (normalizedCode.Length != 32 || !normalizedCode.All(Uri.IsHexDigit))
            throw new SoftBoxAuthenticationException("اسم المستخدم أو رمز الدعم غير صحيح.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            var account = data.Accounts.FirstOrDefault(x =>
                x.UserName.Equals(normalizedUserName, StringComparison.OrdinalIgnoreCase));
            if (account is null || !account.IsEnabled)
                throw new SoftBoxAuthenticationException("اسم المستخدم أو رمز الدعم غير صحيح.");

            var pepper = Convert.FromBase64String(data.RecoveryCodePepper!);
            var codeBytes = Encoding.UTF8.GetBytes(normalizedCode);
            byte[] digest;
            try { digest = HMACSHA256.HashData(pepper, codeBytes); }
            finally
            {
                CryptographicOperations.ZeroMemory(pepper);
                CryptographicOperations.ZeroMemory(codeBytes);
            }

            StoredSupportRecoveryCode? matched = null;
            try
            {
                foreach (var candidate in data.RecoveryCodes ?? [])
                {
                    if (candidate.ConsumedAtUtc is not null)
                        continue;
                    byte[] stored;
                    try { stored = Convert.FromBase64String(candidate.CodeHash); }
                    catch (FormatException) { continue; }
                    try
                    {
                        if (CryptographicOperations.FixedTimeEquals(digest, stored))
                        {
                            matched = candidate;
                            break;
                        }
                    }
                    finally { CryptographicOperations.ZeroMemory(stored); }
                }
            }
            finally { CryptographicOperations.ZeroMemory(digest); }

            if (matched is null)
                throw new SoftBoxAuthenticationException("اسم المستخدم أو رمز الدعم غير صحيح أو سبق استخدامه.");

            var now = DateTimeOffset.UtcNow;
            SetPassword(account, newPassword);
            account.FailedLoginAttempts = 0;
            account.LockedUntilUtc = null;
            account.LastLoginAtUtc = now;
            account.UpdatedAtUtc = now;
            matched.ConsumedAtUtc = now;
            AddAudit(data, null, account.Id, "PasswordRecoveredWithSupportCode", account.UserName,
                "أعيد تعيين كلمة مرور الحساب باستخدام رمز دعم أحادي الاستخدام. لم يُسجل الرمز أو كلمة المرور.");
            await SaveLockedAsync(data, cancellationToken);
            return ToAuthenticatedUser(account, now);
        }
        finally { _gate.Release(); }
    }

    public async Task ChangeOwnPasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (currentPassword is null) throw new ArgumentNullException(nameof(currentPassword));
        ValidatePassword(newPassword);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            var account = data.Accounts.FirstOrDefault(x => x.Id == userId)
                ?? throw new KeyNotFoundException("الحساب الحالي غير موجود.");
            if (!account.IsEnabled)
                throw new SoftBoxAuthenticationException("الحساب معطل.");
            if (!VerifyPassword(currentPassword, account.PasswordSalt, account.PasswordHash, account.PasswordIterations))
                throw new SoftBoxAuthenticationException("كلمة المرور الحالية غير صحيحة.");

            SetPassword(account, newPassword);
            account.FailedLoginAttempts = 0;
            account.LockedUntilUtc = null;
            account.UpdatedAtUtc = DateTimeOffset.UtcNow;
            AddAudit(data, account.Id, account.Id, "PasswordChanged", account.UserName,
                "غيّر المستخدم كلمة مرور حسابه.");
            await SaveLockedAsync(data, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<SoftBoxUserAuditEntry>> GetAuditAsync(
        Guid actorUserId,
        int take = 250,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(take), "عدد سجلات التدقيق يجب أن يكون بين 1 و1000.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            RequireAdministrator(data, actorUserId);
            return data.Audit
                .OrderByDescending(x => x.AtUtc)
                .Take(take)
                .ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task RecordActionInvokedAsync(
        Guid userId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actionId) || actionId.Length > 100)
            throw new ArgumentException("معرف الإجراء غير صالح.", nameof(actionId));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            var account = data.Accounts.FirstOrDefault(x => x.Id == userId)
                ?? throw new KeyNotFoundException("الحساب الحالي غير موجود.");
            if (!account.IsEnabled)
                throw new SoftBoxAuthenticationException("الحساب معطل.");

            AddAudit(data, account.Id, account.Id, "ModuleActionInvoked", actionId,
                $"بدأ المستخدم فتح الأداة المرتبطة بالإجراء {actionId}.");
            await SaveLockedAsync(data, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task RecordSessionLockedAsync(Guid userId, CancellationToken cancellationToken = default)
        => await RecordUserEventAsync(userId, "SessionLocked", "تم قفل جلسة SoftBox.", cancellationToken);

    public async Task RecordSessionUnlockedAsync(Guid userId, CancellationToken cancellationToken = default)
        => await RecordUserEventAsync(userId, "SessionUnlocked", "تم فتح جلسة SoftBox بعد إعادة المصادقة.", cancellationToken);

    public async Task RecordAccessDeniedAsync(Guid userId, string actionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actionId) || actionId.Length > 100)
            throw new ArgumentException("معرف الإجراء غير صالح.", nameof(actionId));
        await RecordUserEventAsync(userId, "AccessDenied", $"محاولة تنفيذ إجراء دون الصلاحية المطلوبة: {actionId}.", cancellationToken);
    }

    private async Task RecordUserEventAsync(Guid userId, string eventType, string description, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var data = await LoadLockedAsync(cancellationToken);
            var account = data.Accounts.FirstOrDefault(x => x.Id == userId)
                ?? throw new KeyNotFoundException("الحساب الحالي غير موجود.");
            AddAudit(data, account.Id, account.Id, eventType, account.UserName, description);
            await SaveLockedAsync(data, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task<SoftBoxAccountData> LoadLockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
            return new SoftBoxAccountData();

        var info = new FileInfo(_storePath);
        if (info.Length is <= 0 or > MaximumStoreBytes)
            throw new InvalidDataException("ملف حسابات SoftBox فارغ أو تجاوز الحد الآمن للحجم.");

        var storedBytes = await File.ReadAllBytesAsync(_storePath, cancellationToken);
        var jsonBytes = WindowsUserDataProtection.UnprotectOrLegacy(
            storedBytes, "SoftBoxAccountStore.v1", out var wasProtected);
        CryptographicOperations.ZeroMemory(storedBytes);

        SoftBoxAccountData data;
        try
        {
            data = JsonSerializer.Deserialize<SoftBoxAccountData>(jsonBytes, _json)
                ?? throw new InvalidDataException("ملف حسابات SoftBox غير صالح.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(jsonBytes);
        }

        if (data.SchemaVersion != 1)
            throw new InvalidDataException($"إصدار ملف الحسابات غير مدعوم: {data.SchemaVersion}.");
        data.Accounts ??= [];
        data.Audit ??= [];
        if (data.Accounts.Count > MaximumAccounts || data.Audit.Count > MaximumAuditEntries ||
            (data.RecoveryCodes?.Count ?? 0) > MaximumSupportRecoveryCodes)
            throw new InvalidDataException("ملف حسابات SoftBox يحتوي على عدد عناصر غير آمن.");

        // Older account stores did not contain a recovery-code pepper. Add it without
        // changing any existing account, password verifier or audit entry.
        var needsRecoveryMigration = !TryDecodeVerifier(data.RecoveryCodePepper, 32);
        if (needsRecoveryMigration)
            data.RecoveryCodePepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        data.RecoveryCodes ??= [];
        foreach (var recoveryCode in data.RecoveryCodes)
        {
            if (recoveryCode.Id == Guid.Empty || !TryDecodeVerifier(recoveryCode.CodeHash, 32))
                throw new InvalidDataException("ملف رموز استرداد SoftBox يحتوي على بيانات غير صالحة.");
        }
        var duplicateNames = data.Accounts.GroupBy(x => x.UserName, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1);
        if (duplicateNames)
            throw new InvalidDataException("ملف الحسابات يحتوي على أسماء مستخدمين مكررة.");
        foreach (var account in data.Accounts)
        {
            _ = NormalizeUserName(account.UserName);
            ValidateRole(account.Role);
            if (!TryDecodeVerifier(account.PasswordSalt, SaltBytes) ||
                !TryDecodeVerifier(account.PasswordHash, PasswordBytes) ||
                account.PasswordIterations is < 300_000 or > 1_000_000 ||
                account.FailedLoginAttempts is < 0 or > MaximumFailedAttempts)
                throw new InvalidDataException($"بيانات مصادقة الحساب {account.UserName} غير صالحة.");
        }

        if (!wasProtected || needsRecoveryMigration)
            await SaveLockedAsync(data, cancellationToken);
        return data;
    }

    private async Task SaveLockedAsync(SoftBoxAccountData data, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storePath)!;
        Directory.CreateDirectory(directory);
        data.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var temporary = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        byte[]? jsonBytes = null;
        byte[]? protectedBytes = null;
        try
        {
            jsonBytes = JsonSerializer.SerializeToUtf8Bytes(data, _json);
            protectedBytes = WindowsUserDataProtection.Protect(jsonBytes, "SoftBoxAccountStore.v1");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(protectedBytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _storePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
        finally
        {
            if (jsonBytes is not null) CryptographicOperations.ZeroMemory(jsonBytes);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private StoredSoftBoxUserAccount CreateAccount(string userName, string displayName, SoftBoxUserRole role, string password)
    {
        var account = new StoredSoftBoxUserAccount
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = displayName.Trim(),
            Role = role,
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        SetPassword(account, password);
        return account;
    }

    private void SetPassword(StoredSoftBoxUserAccount account, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[]? hash = null;
        try
        {
            account.PasswordSalt = Convert.ToBase64String(salt);
            hash = ComputePasswordHash(password, salt, _passwordIterations);
            account.PasswordHash = Convert.ToBase64String(hash);
            account.PasswordIterations = _passwordIterations;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            if (hash is not null)
                CryptographicOperations.ZeroMemory(hash);
        }
    }

    private static byte[] ComputePasswordHash(string password, byte[] salt, int iterations)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, iterations, HashAlgorithmName.SHA256, PasswordBytes);
        }
        finally { CryptographicOperations.ZeroMemory(passwordBytes); }
    }

    private static bool VerifyPassword(string password, string encodedSalt, string encodedHash, int iterations)
    {
        var salt = Convert.FromBase64String(encodedSalt);
        var expected = Convert.FromBase64String(encodedHash);
        var actual = ComputePasswordHash(password, salt, iterations);
        try { return CryptographicOperations.FixedTimeEquals(actual, expected); }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    private static void PerformDummyPasswordWork(string password)
    {
        var salt = SHA256.HashData(Encoding.UTF8.GetBytes("SoftBox local authentication timing equalizer"))[..SaltBytes];
        var hash = ComputePasswordHash(password, salt, DefaultPasswordIterations);
        CryptographicOperations.ZeroMemory(hash);
        CryptographicOperations.ZeroMemory(salt);
    }

    private static StoredSoftBoxUserAccount RequireAdministrator(SoftBoxAccountData data, Guid actorUserId)
    {
        var actor = data.Accounts.FirstOrDefault(x => x.Id == actorUserId)
            ?? throw new UnauthorizedAccessException("الحساب الحالي غير موجود.");
        if (!actor.IsEnabled || actor.Role != SoftBoxUserRole.Administrator)
            throw new UnauthorizedAccessException("هذه العملية متاحة للمدير فقط.");
        return actor;
    }

    private static AuthenticatedSoftBoxUser ToAuthenticatedUser(StoredSoftBoxUserAccount account, DateTimeOffset? at = null)
        => new(account.Id, account.UserName, account.DisplayName, account.Role, at ?? DateTimeOffset.UtcNow);

    private static SoftBoxUserSummary ToSummary(StoredSoftBoxUserAccount account)
        => new(account.Id, account.UserName, account.DisplayName, account.Role, account.IsEnabled,
            account.CreatedAtUtc, account.UpdatedAtUtc, account.LastLoginAtUtc,
            account.LockedUntilUtc, account.FailedLoginAttempts);

    private static void AddAudit(SoftBoxAccountData data, Guid? actor, Guid? subject,
        string eventType, string reference, string description)
    {
        data.Audit.Add(new SoftBoxUserAuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow,
            actor, subject, eventType, reference, description));
        if (data.Audit.Count > MaximumAuditEntries)
            data.Audit.RemoveRange(0, data.Audit.Count - MaximumAuditEntries);
    }

    public static string NormalizeUserName(string? value)
    {
        var userName = (value ?? string.Empty).Trim();
        if (!UserNamePattern.IsMatch(userName))
            throw new ArgumentException("اسم المستخدم يجب أن يكون من 3 إلى 32 حرفًا ويستخدم الإنجليزية أو الأرقام أو النقطة أو الشرطة أو الشرطة السفلية.", nameof(value));
        return userName.ToLowerInvariant();
    }

    private static string NormalizeUserNameForLookup(string? value)
    {
        var userName = (value ?? string.Empty).Trim();
        return userName.Length is >= 1 and <= 64 ? userName.ToLowerInvariant() : "\0invalid";
    }

    public static void ValidateDisplayName(string? value)
    {
        var displayName = value?.Trim() ?? string.Empty;
        if (displayName.Length is < 2 or > 80 || displayName.Any(char.IsControl))
            throw new ArgumentException("الاسم الظاهر مطلوب ويجب أن يكون بين حرفين و80 حرفًا.", nameof(value));
    }

    public static void ValidatePassword(string? password)
    {
        if (password is null || password.Length < MinimumPasswordLength || password.Length > 128 || password.Any(char.IsControl))
            throw new ArgumentException($"كلمة المرور يجب أن تكون من {MinimumPasswordLength} إلى 128 حرفًا، ولا تحتوي على محارف تحكم.", nameof(password));
    }

    private static void ValidateRole(SoftBoxUserRole role)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role), "دور الحساب غير معروف.");
    }

    private static bool TryDecodeVerifier(string? encoded, int expectedLength)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try { return Convert.FromBase64String(encoded).Length == expectedLength; }
        catch (FormatException) { return false; }
    }

    public static string RoleLabel(SoftBoxUserRole role) => role switch
    {
        SoftBoxUserRole.Administrator => "مدير",
        SoftBoxUserRole.Technician => "فني",
        SoftBoxUserRole.ReadOnly => "قراءة فقط",
        _ => "غير معروف"
    };

    private sealed class SoftBoxAccountData
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<StoredSoftBoxUserAccount> Accounts { get; set; } = [];
        public List<SoftBoxUserAuditEntry> Audit { get; set; } = [];
        public string? RecoveryCodePepper { get; set; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        public List<StoredSupportRecoveryCode> RecoveryCodes { get; set; } = [];
    }

    private sealed class StoredSupportRecoveryCode
    {
        public Guid Id { get; set; }
        public string CodeHash { get; set; } = "";
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset? ConsumedAtUtc { get; set; }
    }

    private sealed class StoredSoftBoxUserAccount
    {
        public Guid Id { get; set; }
        public string UserName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public SoftBoxUserRole Role { get; set; }
        public bool IsEnabled { get; set; }
        public string PasswordSalt { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public int PasswordIterations { get; set; }
        public int FailedLoginAttempts { get; set; }
        public DateTimeOffset? LockedUntilUtc { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public DateTimeOffset? LastLoginAtUtc { get; set; }
    }
}
