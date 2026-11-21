using SoftBox.PhoneLab.Core.Firmware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record SamsungOdinUiAutomationReport(
    SamsungOdinAutomationDecision Decision,
    int? ProcessId,
    string? WindowTitle,
    string? ComPort,
    bool StartInvoked);

/// <summary>
/// Uses Windows UI Automation accessibility metadata only. No screen coordinates,
/// simulated key presses, window-message injection or blind clicks are used.
/// Unsupported Odin builds fail closed when package names or controls are not exposed.
/// </summary>
public sealed class SamsungOdinUiAutomationService
{
    private static readonly Regex ComPortPattern = new(@"\bCOM[1-9][0-9]*\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public SamsungOdinUiAutomationReport Inspect(
        string selectedOdinExecutable,
        IReadOnlyList<string> expectedPackagePaths,
        bool operatorConfirmedCompatibility,
        bool operatorConfirmedBackup,
        bool operatorConfirmedComPortMatchesSelectedDevice)
    {
        var capture = Capture(selectedOdinExecutable, expectedPackagePaths,
            operatorConfirmedCompatibility, operatorConfirmedBackup, operatorConfirmedComPortMatchesSelectedDevice);
        return capture.Report;
    }

    public SamsungOdinUiAutomationReport InvokeStart(
        string selectedOdinExecutable,
        IReadOnlyList<string> expectedPackagePaths,
        bool operatorConfirmedCompatibility,
        bool operatorConfirmedBackup,
        bool operatorConfirmedComPortMatchesSelectedDevice)
    {
        var capture = Capture(selectedOdinExecutable, expectedPackagePaths,
            operatorConfirmedCompatibility, operatorConfirmedBackup, operatorConfirmedComPortMatchesSelectedDevice);
        if (!capture.Report.Decision.CanInvokeStart)
            return capture.Report;

        if (capture.StartButton is null)
            return Failed(capture.Report, "تعذر الاحتفاظ بمرجع زر Start الذي تم فحصه.");

        try
        {
            if (!capture.StartButton.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) ||
                pattern is not InvokePattern invokePattern)
                return Failed(capture.Report, "زر Start لا يدعم InvokePattern في إصدار Odin الحالي.");

            invokePattern.Invoke();
            return capture.Report with
            {
                StartInvoked = true,
                Decision = capture.Report.Decision with
                {
                    VerifiedEvidence = capture.Report.Decision.VerifiedEvidence
                        .Append("تم إرسال Invoke واحد لزر Start عبر Windows UI Automation؛ هذا لا يثبت نجاح التفليش.")
                        .ToArray()
                }
            };
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or
                                   UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return Failed(capture.Report, "تعذر استدعاء زر Start بأمان: " + ex.Message);
        }
    }

    private static (SamsungOdinUiAutomationReport Report, AutomationElement? StartButton) Capture(
        string selectedOdinExecutable,
        IReadOnlyList<string> expectedPackagePaths,
        bool operatorConfirmedCompatibility,
        bool operatorConfirmedBackup,
        bool operatorConfirmedComPortMatchesSelectedDevice)
    {
        var expectedPackages = (expectedPackagePaths ?? Array.Empty<string>())
            .Select(path => Path.GetFileName(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        if (string.IsNullOrWhiteSpace(selectedOdinExecutable) || !File.Exists(selectedOdinExecutable))
        {
            var invalid = SamsungOdinAutomationPolicy.Evaluate(new SamsungOdinAutomationEvidence(
                selectedOdinExecutable ?? "", "", "", 0, false, false,
                expectedPackages, Array.Empty<string>(),
                operatorConfirmedCompatibility, operatorConfirmedBackup)
            {
                OperatorConfirmedComPortMatchesSelectedDevice = operatorConfirmedComPortMatchesSelectedDevice
            });
            invalid = invalid with
            {
                Blockers = invalid.Blockers.Append("ملف Odin المحدد غير موجود أو غير صالح.").ToArray(),
                CanInvokeStart = false
            };
            return (new SamsungOdinUiAutomationReport(invalid, null, null, null, false), null);
        }

        var windows = new List<(int ProcessId, AutomationElement Window, string ActualPath)>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var normalizedExpected = Path.GetFullPath(selectedOdinExecutable);
                var normalizedActual = Path.GetFullPath(path);
                if (!normalizedExpected.Equals(normalizedActual, StringComparison.OrdinalIgnoreCase))
                    continue;

                var topLevel = AutomationElement.RootElement.FindAll(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
                foreach (AutomationElement candidate in topLevel)
                {
                    var title = SafeName(candidate);
                    if (title.Contains("Odin", StringComparison.OrdinalIgnoreCase))
                        windows.Add((process.Id, candidate, normalizedActual));
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or
                                       System.ComponentModel.Win32Exception or
                                       UnauthorizedAccessException or
                                       System.Runtime.InteropServices.COMException)
            {
                // An inaccessible process is not considered evidence.
            }
            finally
            {
                process.Dispose();
            }
        }

        var unique = windows
            .GroupBy(item => item.ProcessId)
            .Select(group => group.First())
            .ToArray();

        if (unique.Length != 1)
        {
            var blocked = SamsungOdinAutomationPolicy.Evaluate(new SamsungOdinAutomationEvidence(
                selectedOdinExecutable ?? "",
                "",
                "",
                0,
                false,
                false,
                expectedPackages,
                Array.Empty<string>(),
                operatorConfirmedCompatibility,
                operatorConfirmedBackup)
            {
                OperatorConfirmedComPortMatchesSelectedDevice = operatorConfirmedComPortMatchesSelectedDevice
            });
            blocked = blocked with
            {
                Blockers = blocked.Blockers.Append(
                    unique.Length == 0
                        ? "لم يتم العثور على نافذة Odin قابلة للفحص تابعة للملف المحدد. شغّل Odin بنفس مستوى الصلاحيات الخاص بـ SoftBox."
                        : "تم العثور على أكثر من نافذة Odin للعملية المحددة؛ تم رفض الاختيار لتجنب استهداف نافذة غير صحيحة.")
                    .ToArray(),
                CanInvokeStart = false
            };
            return (new SamsungOdinUiAutomationReport(blocked, null, null, null, false), null);
        }

        var match = unique[0];
        var descendants = match.Window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.IsControlElementProperty, true));

        var visibleText = new List<string> { SafeName(match.Window) };
        var startButtons = new List<AutomationElement>();
        foreach (AutomationElement element in descendants)
        {
            var name = SafeName(element);
            if (name.Length > 0)
                visibleText.Add(name);

            try
            {
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) &&
                    pattern is ValuePattern valuePattern &&
                    !string.IsNullOrWhiteSpace(valuePattern.Current.Value))
                    visibleText.Add(valuePattern.Current.Value);
            }
            catch (ElementNotAvailableException) { }

            try
            {
                if (element.Current.ControlType == ControlType.Button &&
                    name.Equals("Start", StringComparison.OrdinalIgnoreCase))
                    startButtons.Add(element);
            }
            catch (ElementNotAvailableException) { }
        }

        var startEnabled = false;
        var startSupportsInvoke = false;
        if (startButtons.Count == 1)
        {
            try
            {
                startEnabled = startButtons[0].Current.IsEnabled;
                startSupportsInvoke = startButtons[0].TryGetCurrentPattern(InvokePattern.Pattern, out _);
            }
            catch (ElementNotAvailableException) { }
        }

        var allText = string.Join("\n", visibleText);
        var com = ComPortPattern.Match(allText);
        var evidence = new SamsungOdinAutomationEvidence(
            selectedOdinExecutable ?? "",
            match.ActualPath,
            SafeName(match.Window),
            startButtons.Count,
            startEnabled,
            com.Success,
            expectedPackages,
            visibleText,
            operatorConfirmedCompatibility,
            operatorConfirmedBackup)
        {
            StartButtonSupportsInvokePattern = startSupportsInvoke,
            OperatorConfirmedComPortMatchesSelectedDevice = operatorConfirmedComPortMatchesSelectedDevice
        };
        var decision = SamsungOdinAutomationPolicy.Evaluate(evidence);
        if (startButtons.Count == 1 && !startSupportsInvoke)
        {
            decision = decision with
            {
                Blockers = decision.Blockers.Append("زر Start لا يدعم InvokePattern؛ لن يتم استخدام نقرات بديلة أو إحداثيات شاشة.")
                    .ToArray(),
                CanInvokeStart = false
            };
        }

        return (new SamsungOdinUiAutomationReport(
            decision, match.ProcessId, SafeName(match.Window),
            com.Success ? com.Value.ToUpperInvariant() : null, false),
            startButtons.Count == 1 ? startButtons[0] : null);
    }

    private static string SafeName(AutomationElement element)
    {
        try { return element.Current.Name ?? ""; }
        catch (ElementNotAvailableException) { return ""; }
    }

    private static SamsungOdinUiAutomationReport Failed(
        SamsungOdinUiAutomationReport report, string reason)
        => report with
        {
            StartInvoked = false,
            Decision = report.Decision with
            {
                CanInvokeStart = false,
                Blockers = report.Decision.Blockers.Append(reason).ToArray()
            }
        };
}
