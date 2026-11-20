using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Runs one bounded read-only Android diagnostic sequence and presents the actual command outputs.
/// </summary>
public sealed class OneClickDiagnosticWindow : Window
{
    private readonly DeviceQuickDiagnosticService _service;
    private readonly string _serial;
    private readonly DeviceSnapshot? _device;
    private readonly AgentSnapshot? _agent;
    private readonly IReadOnlyList<ComponentDiagnostic> _components;
    private readonly TextBox _output = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _runButton;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _operation;
    private OneClickDiagnosticReport? _report;

    public OneClickDiagnosticWindow(
        AdbClient adb,
        string serial,
        DeviceSnapshot? device,
        AgentSnapshot? agent,
        IReadOnlyList<ComponentDiagnostic> components)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        _serial = serial;
        _device = device;
        _agent = agent;
        _components = components ?? [];
        _service = new DeviceQuickDiagnosticService(new AndroidDeviceToolsService(adb));

        Title = $"SoftBox | الفحص الشامل — {LocalMask(serial)}";
        Width = 1050;
        Height = 770;
        MinWidth = 820;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _runButton = ButtonControl("بدء الفحص الشامل", "#14517A", Run_Click);
        _saveButton = ButtonControl("حفظ تقرير JSON", "#176044", Save_Click);
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
            Text = "الفحص الشامل بزر واحد",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "ONE-CLICK DIAGNOSTIC • READ-ONLY ADB • DEVICE-SCOPED REPORT",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "الجهاز: " + LocalMask(_serial) + ". سيتم تنفيذ قراءات النظام والبطارية والتخزين ووقت التشغيل وقائمة التطبيقات الخارجية فقط. لا يوجد حذف أو تثبيت أو تفليش أو إعادة ضبط.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        buttons.Children.Add(_runButton);
        buttons.Children.Add(_saveButton);
        buttons.Children.Add(_cancelButton);
        DockPanel.SetDock(buttons, Dock.Top);
        root.Children.Add(buttons);

        _output.IsReadOnly = true;
        _output.AcceptsReturn = true;
        _output.TextWrapping = TextWrapping.NoWrap;
        _output.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _output.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _output.Background = Brush("#071625");
        _output.Foreground = Brush("#DCEBFA");
        _output.BorderBrush = Brush("#263D58");
        _output.Padding = new Thickness(10);
        _output.FontFamily = new FontFamily("Consolas");
        _output.FontSize = 10;
        _output.Text = "اضغط بدء الفحص لتنفيذ سلسلة القراءات الآمنة.";
        root.Children.Add(_output);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Text = "بانتظار موافقتك على بدء الفحص.";
        _status.Foreground = Brush("#9FC7E8");
        _status.FontSize = 11;
        footer.Children.Add(_status);
        _progress.Height = 4;
        _progress.IsIndeterminate = true;
        _progress.Visibility = Visibility.Collapsed;
        _progress.Foreground = Brush("#38BDF8");
        footer.Children.Add(_progress);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        return root;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;

        var confirmation = MessageBox.Show(this,
            "سيتم تنفيذ خمسة أوامر قراءة فقط على الجهاز المحدد. لا تتضمن العملية أي تعديل لبيانات الهاتف. هل تبدأ؟",
            "تأكيد الفحص الشامل", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (confirmation != MessageBoxResult.Yes)
            return;

        _operation = new CancellationTokenSource();
        SetBusy(true, "جارٍ بدء الفحص الشامل...");
        try
        {
            var progress = new Progress<string>(message => _status.Text = message);
            _report = await _service.RunAsync(_serial, _device, _agent, _components, progress, _operation.Token);
            _output.Text = DeviceQuickDiagnosticService.Serialize(_report);
            _saveButton.IsEnabled = true;
            var passed = _report.Steps.Count(step => step.Success);
            var failed = _report.Steps.Count - passed;
            _status.Text = $"اكتمل الفحص: {passed} قراءات نجحت، {failed} تعذرت. لم يتم تعديل الهاتف.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء الفحص الشامل.";
        }
        catch (Exception ex)
        {
            _status.Text = "فشل الفحص الشامل: " + ex.Message;
            MessageBox.Show(this, ex.Message, "One-Click Diagnostic",
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
        if (_report is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "حفظ تقرير الفحص الشامل",
            Filter = "JSON Report (*.json)|*.json",
            FileName = $"SoftBox-Diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, DeviceQuickDiagnosticService.Serialize(_report));
            _status.Text = "تم حفظ التقرير: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "تعذر حفظ التقرير",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _runButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private static string LocalMask(string serial)
        => serial.Length > 4 ? "••••" + serial[^4..] : "••••";

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 110,
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
