using System.Globalization;
using System.Text.RegularExpressions;

namespace SoftBox.PhoneLab.Core.Models;

/// <summary>Portable business rules used by the offline workshop and tested without a UI.</summary>
public static partial class WorkshopInternationalRules
{
    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyCodePattern();

    public static string NormalizeCurrencyCode(string code)
    {
        var normalized = (code ?? "").Trim().ToUpperInvariant();
        if (!CurrencyCodePattern().IsMatch(normalized))
            throw new ArgumentException("Currency must be a three-letter ISO 4217 code.", nameof(code));
        return normalized;
    }

    public static string NormalizeCultureName(string cultureName)
    {
        var normalized = (cultureName ?? "").Trim();
        try { return CultureInfo.GetCultureInfo(normalized).Name; }
        catch (CultureNotFoundException) { throw new ArgumentException("Culture name is not supported by this runtime.", nameof(cultureName)); }
    }

    public static string FormatMoney(decimal amount, string currencyCode, string cultureName)
    {
        var currency = NormalizeCurrencyCode(currencyCode);
        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(cultureName); }
        catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
        return $"{amount.ToString("N2", culture)} {currency}";
    }

    public static DateTimeOffset? CalculateWarrantyEnd(DateTimeOffset? deliveredAtUtc, int warrantyMonths)
    {
        if (warrantyMonths is < 0 or > 120)
            throw new ArgumentOutOfRangeException(nameof(warrantyMonths), "Warranty duration must be between 0 and 120 months.");
        if (deliveredAtUtc is null || warrantyMonths == 0) return null;
        return deliveredAtUtc.Value.AddMonths(warrantyMonths);
    }

    public static bool IsWarrantyActive(DateTimeOffset? warrantyEndsAtUtc, DateTimeOffset nowUtc)
        => warrantyEndsAtUtc.HasValue && warrantyEndsAtUtc.Value >= nowUtc;
}
