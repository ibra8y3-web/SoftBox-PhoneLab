namespace SoftBox.PhoneLab.Core.Diagnostics;

public enum ServiceTransportKind
{
    QualcommEdl,
    MediaTekPreloaderBrom,
    Unknown
}

public enum ServiceTransportState
{
    NotDetected,
    DetectedWithDriverProblem,
    DetectedDiagnosticOnly
}

public sealed record ServiceTransportObservation(
    string InstanceId,
    string FriendlyName,
    string VendorId,
    string ProductId,
    int ProblemCode,
    string ServiceName);

public sealed record ServiceTransportReadiness(
    ServiceTransportKind Kind,
    ServiceTransportState State,
    string FriendlyName,
    string InstanceId,
    string HardwareId,
    string DriverStatus,
    bool FlashAllowed,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> MissingRequirements);

/// <summary>
/// Converts Windows PnP observations into a safe transport-readiness report.
/// It never opens EDL/BROM protocols or claims programmer/DA compatibility.
/// </summary>
public static class ServiceTransportReadinessPolicy
{
    public static IReadOnlyList<ServiceTransportReadiness> Evaluate(
        IEnumerable<ServiceTransportObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var results = new List<ServiceTransportReadiness>();
        foreach (var observation in observations)
        {
            var kind = ResolveKind(observation.VendorId, observation.ProductId);
            if (kind == ServiceTransportKind.Unknown)
                continue;

            var hardwareId = observation.VendorId.ToUpperInvariant() + ":" + observation.ProductId.ToUpperInvariant();
            var driverProblem = observation.ProblemCode != 0;
            var findings = new List<string>
            {
                kind == ServiceTransportKind.QualcommEdl
                    ? "تم رصد USB ID الشائع Qualcomm 05C6:9008؛ هذا يثبت ظهور PnP فقط ولا يثبت اتصال EDL صالحًا."
                    : "تم رصد USB ID الشائع MediaTek 0E8D:0003؛ هذا يثبت ظهور PnP فقط ولا يثبت جلسة BROM/Preloader صالحة.",
                driverProblem
                    ? $"أبلغ Windows عن رمز مشكلة PnP: {observation.ProblemCode}."
                    : "Windows يعرض عقدة الجهاز دون رمز مشكلة PnP."
            };
            if (!string.IsNullOrWhiteSpace(observation.ServiceName))
                findings.Add("Driver service: " + observation.ServiceName);
            else
                findings.Add("لم يعرض Windows اسم خدمة Driver لهذه العقدة.");

            var missing = new List<string>
            {
                "هوية موديل/لوحة دقيقة مرتبطة بوسيلة تحقق مستقلة.",
                "ملف Programmer (Qualcomm) أو Download Agent (MediaTek) موقع ومتوافق مع هذا الموديل.",
                "دليل تفويض OEM وحالة Secure Boot/Authentication المسموح بها.",
                "إجراء استعادة رسمي موثق ومختبر لهذا الإصدار."
            };
            results.Add(new ServiceTransportReadiness(
                kind,
                driverProblem ? ServiceTransportState.DetectedWithDriverProblem : ServiceTransportState.DetectedDiagnosticOnly,
                observation.FriendlyName,
                observation.InstanceId,
                hardwareId,
                driverProblem ? "Problem code " + observation.ProblemCode : "PnP present",
                FlashAllowed: false,
                findings,
                missing));
        }

        return results;
    }

    private static ServiceTransportKind ResolveKind(string vendorId, string productId)
    {
        if (vendorId.Equals("05C6", StringComparison.OrdinalIgnoreCase) &&
            productId.Equals("9008", StringComparison.OrdinalIgnoreCase))
            return ServiceTransportKind.QualcommEdl;
        if (vendorId.Equals("0E8D", StringComparison.OrdinalIgnoreCase) &&
            productId.Equals("0003", StringComparison.OrdinalIgnoreCase))
            return ServiceTransportKind.MediaTekPreloaderBrom;
        return ServiceTransportKind.Unknown;
    }
}
