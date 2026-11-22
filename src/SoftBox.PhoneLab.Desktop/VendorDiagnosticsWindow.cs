using Microsoft.Win32;
using SoftBox.PhoneLab.Desktop.Services;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Collects vendor-specific Android system properties from an authorized ADB device.
/// All commands use fixed getprop keys and remain read-only.
/// </summary>
public sealed class VendorDiagnosticsWindow : Window
{
    private readonly AndroidDeviceToolsService _service;
    private readonly AdbClient _adb;
    private readonly string _serial;
    private readonly string _manufacturer;
    private readonly string _model;
    private readonly TextBox _output = new();
    private readonly TextBlock _status = new();
    private readonly Button _scanButton;
    private readonly Button _saveButton;
    private readonly Button? _xiaomiFastbootButton;
    private CancellationTokenSource? _operation;
    private AndroidVendorDiagnostics? _diagnostics;

    public VendorDiagnosticsWindow(AdbClient adb, string serial, string manufacturer, string model)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        _adb = adb;
        _service = new AndroidDeviceToolsService(adb);
        _serial = serial;
        _manufacturer = manufacturer ?? "";
        _model = model ?? "";

        var vendor = AndroidDeviceToolsService.NormalizeVendor(manufacturer);
        Title = $"SoftBox | {vendor} Vendor Diagnostics";
        Width = 920;
        Height = 700;
        MinWidth = 720;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _scanButton = ButtonControl("قراءة خصائص الشركة", "#14517A", Scan_Click);
        _saveButton = ButtonControl("حفظ تقرير JSON", "#176044", Save_Click);
        _xiaomiFastbootButton = vendor == AndroidVendorKind.Xiaomi
            ? ButtonControl("تشخيص Xiaomi Fastboot", "#704B20", XiaomiFastboot_Click)
            : null;
        _saveButton.IsEnabled = false;
        Content = BuildContent(vendor, _model);
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent(AndroidVendorKind vendor, string model)
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = vendor switch
            {
                AndroidVendorKind.Samsung => "Samsung Device Diagnostics",
                AndroidVendorKind.Xiaomi => "Xiaomi / Redmi / POCO Diagnostics",
                AndroidVendorKind.GooglePixel => "Google Pixel Diagnostics",
                AndroidVendorKind.Motorola => "Motorola / Lenovo Diagnostics",
                AndroidVendorKind.OnePlus => "OnePlus Diagnostics",
                AndroidVendorKind.Oppo => "OPPO Diagnostics",
                AndroidVendorKind.Vivo => "vivo Diagnostics",
                AndroidVendorKind.Realme => "realme Diagnostics",
                AndroidVendorKind.Huawei => "Huawei / HONOR Diagnostics",
                AndroidVendorKind.Tecno => "TECNO Diagnostics",
                AndroidVendorKind.Infinix => "Infinix Diagnostics",
                _ => "Android Vendor Diagnostics"
            },
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = $"الجهاز: {model} | الشركة: {manufacturerText()} | ADB: {Mask(_serial)}",
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var warning = new Border
        {
            Background = Brush("#211D2A"),
            BorderBrush = Brush("#66516B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock
            {
                Text = "قراءة فقط لخصائص Android المعروفة للشركة. القيم الفارغة تعني أن الخاصية غير متاحة لهذا النظام. لا يثبت هذا الفحص توافق Firmware أو Download/EDL/BROM، ولا ينفذ تفليشًا أو فتح Bootloader أو تجاوز حماية.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(warning, Dock.Top);
        root.Children.Add(warning);

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        buttons.Children.Add(_scanButton);
        buttons.Children.Add(_saveButton);
        if (_xiaomiFastbootButton is not null)
            buttons.Children.Add(_xiaomiFastbootButton);
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
        _output.Text = "اضغط قراءة خصائص الشركة لجمع البيانات الفعلية من الهاتف.";
        root.Children.Add(_output);

        _status.Text = "لم يتم تنفيذ الفحص بعد.";
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;

        string manufacturerText() => string.IsNullOrWhiteSpace(_manufacturer) ? "غير محدد" : _manufacturer;
    }

    private void XiaomiFastboot_Click(object sender, RoutedEventArgs e)
    {
        if (AndroidDeviceToolsService.NormalizeVendor(_manufacturer) != AndroidVendorKind.Xiaomi)
        {
            _status.Text = "تم منع فتح أدوات Xiaomi لأن هوية الشركة الحالية لا تطابق Xiaomi.";
            return;
        }
        new XiaomiFastbootDiagnosticsWindow(_adb, _serial, _manufacturer, _model)
        {
            Owner = this
        }.ShowDialogEmbedded();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;
        _operation = new CancellationTokenSource();
        _scanButton.IsEnabled = false;
        try
        {
            _status.Text = "جارٍ قراءة خصائص getprop المسموح بها من الجهاز...";
            _diagnostics = await _service.ReadVendorDiagnosticsAsync(_serial, _manufacturer, _operation.Token);
            var lines = new List<string>
            {
                "VENDOR: " + _diagnostics.Vendor,
                "COLLECTED UTC: " + _diagnostics.CollectedAtUtc.ToString("O"),
                "DEVICE: " + Mask(_serial),
                "",
                "AVAILABLE PROPERTIES:"
            };
            lines.AddRange(_diagnostics.AvailableProperties.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item.Key + " = " + item.Value));
            lines.Add("");
            lines.Add("UNAVAILABLE / EMPTY PROPERTIES:");
            lines.AddRange(_diagnostics.UnavailableProperties.OrderBy(item => item, StringComparer.Ordinal));
            lines.Add("");
            lines.Add("No firmware compatibility or service-mode compatibility is inferred from these values.");
            _output.Text = string.Join(Environment.NewLine, lines);
            _saveButton.IsEnabled = true;
            _status.Text = $"تمت قراءة {_diagnostics.AvailableProperties.Count} خاصية. الفارغ: {_diagnostics.UnavailableProperties.Count}.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء قراءة الخصائص.";
        }
        catch (Exception ex)
        {
            _status.Text = "فشلت قراءة خصائص الشركة: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Vendor Diagnostics", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _scanButton.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnostics is null)
            return;
        var dialog = new SaveFileDialog
        {
            Title = "حفظ تقرير خصائص الشركة",
            Filter = "JSON (*.json)|*.json",
            FileName = "SoftBox-Vendor-Diagnostics.json"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var report = new
            {
                DeviceSerialMasked = Mask(_serial),
                _diagnostics.Vendor,
                _diagnostics.CollectedAtUtc,
                _diagnostics.AvailableProperties,
                _diagnostics.UnavailableProperties,
                Limitations = new[]
                {
                    "Read-only Android getprop collection.",
                    "Does not establish firmware authenticity, model compatibility, download mode support, or recovery capability."
                }
            };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            _status.Text = "تم حفظ التقرير: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر حفظ التقرير: " + ex.Message;
        }
    }

    private static string Mask(string serial) => serial.Length > 4 ? "••••" + serial[^4..] : "••••";

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 130,
            Background = Brush(color),
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
