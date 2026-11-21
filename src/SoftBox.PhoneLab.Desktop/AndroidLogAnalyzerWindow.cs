using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Desktop.Services;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Collects a bounded read-only ADB logcat snapshot, analyzes it locally and retains only
/// categorized summaries in the report. Raw log lines are not saved or uploaded.
/// </summary>
public sealed class AndroidLogAnalyzerWindow : Window
{
    private readonly AndroidDeviceToolsService _android;
    private readonly string _serial;
    private readonly TextBox _output = new();
    private readonly TextBlock _status = new();
    private readonly Button _analyzeButton;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _operation;
    private AndroidLogAnalysisResult? _result;

    public AndroidLogAnalyzerWindow(AdbClient adb, string serial)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        _serial = serial;
        _android = new AndroidDeviceToolsService(adb ?? throw new ArgumentNullException(nameof(adb)));

        Title = "SoftBox | محلل سجلات Android";
        Width = 1120;
        Height = 800;
        MinWidth = 850;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _analyzeButton = ButtonControl("جمع وتحليل Logcat", "#14517A", Analyze_Click);
        _saveButton = ButtonControl("حفظ ملخص JSON", "#176044", Save_Click);
        _cancelButton = ButtonControl("إلغاء", "#51333A", Cancel_Click);
        _saveButton.IsEnabled = false;
        _cancelButton.IsEnabled = false;

        Content = BuildContent();
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "محلل سجلات Android",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "ANDROID LOGCAT ANALYSIS • LOCAL ONLY • READ-ONLY ADB",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        var maskedSerial = _serial.Length > 4 ? "••••" + _serial[^4..] : "••••";
        header.Children.Add(new TextBlock
        {
            Text = $"الجهاز: {maskedSerial}. يقرأ آخر 3000 سطر من سجل Android، ثم يجمع مؤشرات الانهيارات وANR والذاكرة والصلاحيات وSELinux والحرارة والتخزين وUSB والشبكة. لا يتم تنفيذ أوامر تعديل.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "تنبيه خصوصية: قد يحتوي Logcat الأصلي على بيانات شخصية أو أسماء تطبيقات ومعرفات. التحليل محلي؛ التقرير المحفوظ يحتوي ملخصات مجمعة فقط ولا يحتوي السطور الخام.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#F0C9A5"),
            Background = Brush("#211D2A"),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(_analyzeButton);
        actions.Children.Add(_saveButton);
        actions.Children.Add(_cancelButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _output.IsReadOnly = true;
        _output.AcceptsReturn = true;
        _output.TextWrapping = TextWrapping.Wrap;
        _output.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _output.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _output.Background = Brush("#071625");
        _output.Foreground = Brush("#DCEBFA");
        _output.BorderBrush = Brush("#263D58");
        _output.Padding = new Thickness(10);
        _output.FontFamily = new FontFamily("Consolas");
        _output.FontSize = 10;
        _output.Text = "اضغط «جمع وتحليل Logcat» لقراءة السجل الحالي وتحليله محليًا.";
        root.Children.Add(_output);

        _status.Text = "بانتظار موافقتك على قراءة السجل.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;

        var confirmation = MessageBox.Show(this,
            "سيقرأ SoftBox آخر 3000 سطر من Logcat عبر ADB للقراءة فقط. قد يتضمن السجل بيانات شخصية. سيظل التحليل على هذا الكمبيوتر، ولن تُرفع السطور الخام أو تُحفظ في التقرير. هل تبدأ؟",
            "تأكيد قراءة سجلات Android", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (confirmation != MessageBoxResult.Yes)
            return;

        _operation = new CancellationTokenSource();
        SetBusy(true, "جارٍ قراءة Logcat من الهاتف...");
        try
        {
            var logcat = await _android.RunReadOnlyPresetAsync(
                _serial, "logcat", _operation.Token);

            _status.Text = "تمت قراءة Logcat؛ جارٍ تصنيف الأحداث وتجميع التكرارات محليًا...";
            _result = AndroidLogAnalysisEngine.Analyze(logcat);
            _output.Text = FormatReport(_result);
            _saveButton.IsEnabled = true;
            _status.Text =
                $"اكتمل التحليل: {_result.LinesAnalyzed:N0} سطرًا تمت مراجعته، " +
                $"{_result.EventGroups:N0} مجموعات أحداث، منها {_result.CriticalEventGroups:N0} حرجة. " +
                "لم يتم حفظ السطور الخام أو تعديل الهاتف.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء جمع وتحليل Logcat.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر جمع أو تحليل سجلات Android: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Android Log Analyzer",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false, _status.Text);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "حفظ ملخص تحليل Logcat",
            Filter = "JSON Summary (*.json)|*.json",
            FileName = $"SoftBox-Logcat-Summary-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(
                _result, new JsonSerializerOptions { WriteIndented = true }));
            _status.Text = "تم حفظ ملخص الأحداث فقط. لم يتم تضمين Logcat الخام: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر حفظ ملخص Logcat",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FormatReport(AndroidLogAnalysisResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("SOFTBOX ANDROID LOGCAT ANALYSIS");
        text.AppendLine("ANALYZED UTC: " + result.AnalyzedAtUtc.ToString("O"));
        text.AppendLine("TOTAL INPUT LINES: " + result.TotalLinesRead.ToString("N0"));
        text.AppendLine("LINES ANALYZED: " + result.LinesAnalyzed.ToString("N0"));
        text.AppendLine("RELEVANT LINES: " + result.RelevantLines.ToString("N0"));
        text.AppendLine("EVENT GROUPS: " + result.EventGroups.ToString("N0"));
        text.AppendLine("CRITICAL GROUPS: " + result.CriticalEventGroups.ToString("N0"));
        text.AppendLine("INPUT TRUNCATED: " + result.InputTruncated);
        text.AppendLine();

        if (result.Events.Count == 0)
            text.AppendLine("No configured failure patterns were detected in the bounded logcat snapshot.");

        foreach (var item in result.Events)
        {
            text.AppendLine($"[{item.Severity}] {item.Category} — {item.Summary}");
            text.AppendLine("TAG: " + item.Tag);
            text.AppendLine("OCCURRENCES: " + item.Occurrences.ToString("N0"));
            text.AppendLine("LAST SEEN: " + (item.LastSeenTimestamp.Length > 0 ? item.LastSeenTimestamp : "timestamp unavailable"));
            text.AppendLine();
        }

        text.AppendLine("LIMITATIONS");
        foreach (var limitation in result.Limitations)
            text.AppendLine("- " + limitation);

        text.AppendLine();
        text.AppendLine("REPORT CONTENT: grouped summaries only; raw log lines are not included.");
        return text.ToString();
    }

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _analyzeButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
        if (!busy)
            _saveButton.IsEnabled = _result is not null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => _operation?.Cancel();

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 140,
            Background = Brush(background),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold
        };
        button.Click += handler;
        return button;
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
