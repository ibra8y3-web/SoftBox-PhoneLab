using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SoftBox.PhoneLab.Core.Recovery;

public sealed record OfficialRecoveryCaseInput(
    string Manufacturer,
    string Model,
    bool OwnerOrAuthorized,
    bool ProofOfOwnershipAvailable,
    bool OrganizationAdministratorAuthorized,
    bool CarrierAccountHolderAuthorized);

public sealed record OfficialRecoveryCaseResult(
    string CaseReference,
    OfficialRecoveryCaseKind Kind,
    bool ReadyForOfficialContact,
    IReadOnlyList<string> ValidationErrors,
    string Markdown);

/// <summary>
/// Validates authorization declarations and prepares a local checklist for an official
/// recovery authority. It has no device-write, account, modem or lock-removal capability.
/// </summary>
public static class OfficialRecoveryCaseService
{
    public static OfficialRecoveryCaseResult Build(
        OfficialRecoveryCaseKind kind,
        OfficialRecoveryCaseInput input,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var workflow = OfficialRecoveryWorkflowCatalog.Get(kind);
        var manufacturer = Normalize(input.Manufacturer, 80);
        var model = Normalize(input.Model, 80);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(manufacturer))
            errors.Add("اسم الشركة المصنّعة مطلوب.");
        if (string.IsNullOrWhiteSpace(model))
            errors.Add("الموديل مطلوب.");
        if (!input.OwnerOrAuthorized)
            errors.Add("يجب تأكيد أنك المالك أو لديك تفويض صريح من المالك.");
        if (kind == OfficialRecoveryCaseKind.MdmRelease && !input.OrganizationAdministratorAuthorized)
            errors.Add("إزالة MDM تتطلب تأكيد تفويض مسؤول المؤسسة أو مزود EMM.");
        if (kind == OfficialRecoveryCaseKind.CarrierNetworkUnlock && !input.CarrierAccountHolderAuthorized)
            errors.Add("طلب فتح الشبكة يتطلب صاحب حساب المشغّل أو مفوضًا عنه.");

        if (errors.Count > 0)
            return new OfficialRecoveryCaseResult(string.Empty, kind, false, errors.AsReadOnly(), string.Empty);

        var timestamp = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var reference = "SBX-" +
                        timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
                        Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var markdown = BuildMarkdown(
            reference,
            timestamp,
            workflow,
            manufacturer,
            model,
            input.ProofOfOwnershipAvailable);

        return new OfficialRecoveryCaseResult(
            reference,
            kind,
            true,
            Array.Empty<string>(),
            markdown);
    }

    private static string BuildMarkdown(
        string reference,
        DateTimeOffset createdAt,
        OfficialRecoveryWorkflow workflow,
        string manufacturer,
        string model,
        bool proofAvailable)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# SoftBox — Official Device Recovery Plan");
        builder.AppendLine();
        builder.AppendLine("- Reference: " + reference);
        builder.AppendLine("- Created (UTC): " + createdAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        builder.AppendLine("- Case: " + workflow.TitleAr);
        builder.AppendLine("- Device: " + manufacturer + " / " + model);
        builder.AppendLine("- Ownership/authorization: confirmed by the operator");
        builder.AppendLine("- Proof of ownership: " + (proofAvailable ? "available for the official authority" : "not declared"));
        builder.AppendLine();
        builder.AppendLine("## Official steps");
        for (var index = 0; index < workflow.Steps.Count; index++)
            builder.AppendLine($"{index + 1}. {workflow.Steps[index]}");

        if (workflow.Resources.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Official resources");
            foreach (var resource in workflow.Resources)
                builder.AppendLine("- [" + resource.Title + "](" + resource.Url + ") — " + resource.Description);
        }

        builder.AppendLine();
        builder.AppendLine("## Safety and scope");
        builder.AppendLine("- This file is a locally generated checklist, not an OEM/carrier approval or proof of ownership.");
        builder.AppendLine("- SoftBox does not bypass FRP, Mi Account, Samsung Account, screen locks, MDM or carrier restrictions.");
        builder.AppendLine("- No IMEI, serial number, password, PIN, recovery code, OTP, IMSI or ICCID is collected in this workflow.");
        builder.AppendLine("- Submit any identifiers required by an authority only through its official secure channel.");
        builder.AppendLine("- No data is erased and no device, account, policy, modem or network state is changed by this case builder.");
        return builder.ToString();
    }

    private static string Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var safe = new string(value
            .Where(character => !char.IsControl(character) && character != '[' && character != ']' && character != (char)96)
            .ToArray()).Trim();
        return safe.Length <= maximumLength ? safe : safe[..maximumLength];
    }
}
