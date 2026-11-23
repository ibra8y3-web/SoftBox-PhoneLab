namespace SoftBox.PhoneLab.Core.Diagnostics;

/// <summary>
/// Normalizes technician-entered text before it is saved to the bounded diagnostic evidence store.
/// New lines and tabs are presentation whitespace; remaining control characters are rejected.
/// </summary>
public static class IPhoneAssessmentInputValidator
{
    public static string NormalizeNote(string? value, int maximumLength = 1000)
    {
        if (maximumLength is < 1 or > 6000)
            throw new ArgumentOutOfRangeException(nameof(maximumLength));

        var normalized = (value ?? string.Empty)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();

        if (normalized.Length > maximumLength || normalized.Any(char.IsControl))
            throw new ArgumentException("الملاحظة أطول من الحد المسموح أو تحتوي محارف تحكم غير صالحة.");

        return normalized;
    }
}
