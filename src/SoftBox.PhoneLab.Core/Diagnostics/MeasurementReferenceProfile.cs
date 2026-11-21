using System.Text.Json;
using SoftBox.PhoneLab.Core.Models;

namespace SoftBox.PhoneLab.Core.Diagnostics;

public sealed record MeasurementReferenceLimit(
    string Name,
    string Unit,
    double Minimum,
    double Maximum);

public sealed record MeasurementReferenceProfile(
    int SchemaVersion,
    string ProfileName,
    string DeviceScope,
    string SourceReference,
    IReadOnlyList<MeasurementReferenceLimit> Limits);

public enum MeasurementLimitState
{
    Passed,
    OutOfRange,
    NotMeasured
}

public sealed record MeasurementLimitResult(
    string Name,
    string Unit,
    double? Actual,
    double Minimum,
    double Maximum,
    MeasurementLimitState State,
    string Explanation);

/// <summary>
/// Validates and compares read-only fixture samples against limits explicitly supplied by a technician.
/// No factory tolerances are invented; missing measurements remain NotMeasured.
/// </summary>
public static class MeasurementReferenceProfilePolicy
{
    private static readonly HashSet<string> AllowedMeasurements = new(StringComparer.OrdinalIgnoreCase)
    {
        "usbVoltageV", "batteryVoltageV", "batteryCurrentA", "boardTemperatureC",
        "usbPowerW", "batteryCapacityMah", "batteryInternalResistanceMilliOhm"
    };

    public static MeasurementReferenceProfile Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 256 * 1024)
            throw new InvalidDataException("ملف حدود القياس فارغ أو يتجاوز 256 KiB.");
        var profile = JsonSerializer.Deserialize<MeasurementReferenceProfile>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 12 })
            ?? throw new InvalidDataException("مخطط ملف حدود القياس غير صالح.");
        Validate(profile);
        return profile;
    }

    public static void Validate(MeasurementReferenceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(profile.ProfileName) || profile.ProfileName.Length > 120 ||
            string.IsNullOrWhiteSpace(profile.DeviceScope) || profile.DeviceScope.Length > 200 ||
            string.IsNullOrWhiteSpace(profile.SourceReference) || profile.SourceReference.Length > 500)
            throw new InvalidDataException("بيانات ملف القياس الأساسية غير صالحة.");
        if (profile.Limits is null || profile.Limits.Count is < 1 or > 100)
            throw new InvalidDataException("ملف القياس يجب أن يحتوي من 1 إلى 100 حد.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var limit in profile.Limits)
        {
            if (limit is null || string.IsNullOrWhiteSpace(limit.Name) || limit.Name.Length > 80 ||
                limit.Name.Any(char.IsControl) || string.IsNullOrWhiteSpace(limit.Unit) || limit.Unit.Length > 20 ||
                !double.IsFinite(limit.Minimum) || !double.IsFinite(limit.Maximum) ||
                limit.Minimum > limit.Maximum || !names.Add(limit.Name))
                throw new InvalidDataException("يوجد حد قياس فارغ أو مكرر أو غير صالح.");
            if (!AllowedMeasurements.Contains(limit.Name) && !limit.Name.StartsWith("rail:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("اسم قياس غير مدعوم: " + limit.Name);
        }
    }

    public static IReadOnlyList<MeasurementLimitResult> Evaluate(
        MeasurementReferenceProfile profile,
        ExternalMeasurementSample sample)
    {
        Validate(profile);
        ArgumentNullException.ThrowIfNull(sample);
        var values = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
        {
            ["usbVoltageV"] = sample.UsbVoltageV,
            ["batteryVoltageV"] = sample.BatteryVoltageV,
            ["batteryCurrentA"] = sample.BatteryCurrentA,
            ["boardTemperatureC"] = sample.BoardTemperatureC,
            ["usbPowerW"] = sample.UsbPowerW,
            ["batteryCapacityMah"] = sample.BatteryCapacityMah,
            ["batteryInternalResistanceMilliOhm"] = sample.BatteryInternalResistanceMilliOhm
        };
        foreach (var rail in sample.Rails ?? [])
            values["rail:" + rail.Key] = rail.Value;

        return profile.Limits.Select(limit =>
        {
            if (!values.TryGetValue(limit.Name, out var actual) || actual is null || !double.IsFinite(actual.Value))
                return new MeasurementLimitResult(limit.Name, limit.Unit, null, limit.Minimum, limit.Maximum,
                    MeasurementLimitState.NotMeasured, "لم يصل قياس لهذا الحقل من جهاز القياس.");
            var passed = actual.Value >= limit.Minimum && actual.Value <= limit.Maximum;
            return new MeasurementLimitResult(limit.Name, limit.Unit, actual, limit.Minimum, limit.Maximum,
                passed ? MeasurementLimitState.Passed : MeasurementLimitState.OutOfRange,
                passed ? "القراءة داخل الحدود التي أدخلها الفني." : "القراءة خارج الحدود التي أدخلها الفني.");
        }).ToArray();
    }
}
