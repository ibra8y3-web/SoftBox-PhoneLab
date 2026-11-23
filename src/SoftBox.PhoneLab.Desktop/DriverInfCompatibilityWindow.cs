using Microsoft.Win32;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Compares an extracted OEM INF against the exact Windows PnP hardware IDs for one device.
/// It does not install, stage, update or remove a driver.
/// </summary>
public sealed class DriverInfCompatibilityWindow : Window
{
    private readonly string[] _hardwareIds;
    private readonly string _manufacturer;
    private readonly string _deviceName;
    private readonly TextBox _infPath = new();
    private readonly TextBox _catalogPath = new();
    private readonly TextBox _report = new();
    private readonly TextBlock _status = new();
    private readonly WindowsDriverSignatureVerificationService _signatureService = new();

    public DriverInfCompatibilityWindow(
        IReadOnlyList<string>? hardwareIds,
        string? manufacturer,
        string? deviceName)
    {
        _hardwareIds = (hardwareIds ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(128)
            .ToArray();
        _manufacturer = string.IsNullOrWhiteSpace(manufacturer) ? "غير معروف" : manufacturer.Trim();
        _deviceName = string.IsNullOrWhiteSpace(deviceName) ? "جهاز USB" : deviceName.Trim();

        Title = "SoftBox | OEM Driver INF Compatibility";
        Width = 960;
        Height = 780;
        MinWidth = 760;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "مطابقة تعريف OEM مع هوية الجهاز وWindows",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        root.Children.Add(Text(
            $"الجهاز: {_manufacturer} • {_deviceName}\nWindows: {WindowsDriverTarget.Current.DisplayName} • Architecture: {WindowsDriverTarget.Current.Architecture}",
            "#B7C9DE", 10));

        var ids = _hardwareIds.Length == 0
            ? "لم تُقرأ Hardware IDs من Windows لهذا الجهاز. لا يمكن إثبات المطابقة بالاسم أو VID/PID فقط."
            : string.Join(Environment.NewLine, _hardwareIds.Take(16));
        root.Children.Add(Panel(
            "Hardware IDs التي قرأها Windows من جهاز PnP المحدد",
            Text(ids, _hardwareIds.Length == 0 ? "#F0C9A5" : "#AFC7DF", 9)));

        var infRow = new DockPanel { Margin = new Thickness(0, 8, 0, 5) };
        StylePathBox(_infPath);
        DockPanel.SetDock(PathButton("اختيار ملف INF", BrowseInf), Dock.Left);
        infRow.Children.Add(_infPath);
        infRow.Children.Add(PathButton("اختيار ملف INF", BrowseInf));
        root.Children.Add(Text("ملف INF بعد فك حزمة الشركة المصنّعة", "#7DD3FC", 10));
        root.Children.Add(infRow);
        root.Children.Add(Button("تحليل Hardware IDs والمعمارية وWindows Build", "#14517A", async (_, _) => await AnalyzeInfAsync()));

        var catRow = new DockPanel { Margin = new Thickness(0, 10, 0, 5) };
        StylePathBox(_catalogPath);
        catRow.Children.Add(_catalogPath);
        catRow.Children.Add(PathButton("اختيار CAT", BrowseCatalog));
        root.Children.Add(Text("ملف CAT المرفق مع الحزمة (اختياري لكنه مطلوب للتحقق من عضوية التوقيع)", "#7DD3FC", 10));
        root.Children.Add(catRow);
        root.Children.Add(Button("تحقق توقيع CAT وعضوية INF عبر Microsoft SignTool", "#20364F", async (_, _) => await VerifyCatalogMembershipAsync()));

        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 9, 0, 7);
        _status.Text = _hardwareIds.Length == 0
            ? "لا تتوفر هوية عتادية دقيقة. افتح هذه الأداة من بطاقة جهاز Windows USB المحدد في Universal Device Discovery."
            : "لم يبدأ التحليل بعد.";
        root.Children.Add(_status);

        _report.IsReadOnly = true;
        _report.AcceptsReturn = true;
        _report.TextWrapping = TextWrapping.Wrap;
        _report.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _report.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _report.MinHeight = 260;
        _report.Padding = new Thickness(10);
        _report.Margin = new Thickness(0, 8, 0, 0);
        _report.Background = Brush("#071625");
        _report.Foreground = Brush("#DCEBFA");
        _report.BorderBrush = Brush("#263D58");
        root.Children.Add(_report);

        root.Children.Add(Text(
            "حدود القرار: نتيجة INF Compatible تعني أن Hardware ID ظهر في قسم Models مناسب للمعمارية ونسخة Windows وفق تحليل INF ثابت. لا تثبت وحدها توقيع الحزمة أو عضوية INF داخل CAT أو جودة المصدر أو اعتماد OEM. تحقق من CAT وعضوية الملفات قبل التفكير في أي تثبيت. SoftBox لا يثبّت التعريف من هذه النافذة.",
            "#EBCB9B", 10));

        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async Task AnalyzeInfAsync()
    {
        if (string.IsNullOrWhiteSpace(_infPath.Text))
        {
            SetStatus("اختر ملف INF أولًا.", false);
            return;
        }
        if (_hardwareIds.Length == 0)
        {
            SetStatus("تعذر إثبات المطابقة: لا توجد Hardware IDs صادرة عن Windows لهذا الجهاز.", false);
            return;
        }

        try
        {
            SetStatus("جارٍ تحليل أقسام [Manufacturer] وModels ومعمارية Windows وBuild...", null);
            var target = WindowsDriverTarget.Current;
            var result = await DriverInfCompatibilityAnalyzer.AnalyzeFileAsync(
                _infPath.Text, _hardwareIds, target);
            var compatible = result.Status == DriverInfMatchStatus.Compatible;
            SetStatus(result.Explanation, compatible);

            var builder = new StringBuilder();
            builder.AppendLine("INF: " + result.InfPath);
            builder.AppendLine("Result: " + result.Status);
            builder.AppendLine("Provider: " + (string.IsNullOrWhiteSpace(result.Provider) ? "غير متاح" : result.Provider));
            builder.AppendLine("DriverVer: " + (string.IsNullOrWhiteSpace(result.DriverVersion) ? "غير متاح" : result.DriverVersion));
            builder.AppendLine("Windows: " + result.WindowsTarget);
            builder.AppendLine("Architecture: " + result.Architecture);
            builder.AppendLine();
            builder.AppendLine("Matched Hardware IDs:");
            builder.AppendLine(result.MatchedHardwareIds.Count == 0
                ? "لا يوجد"
                : string.Join(Environment.NewLine, result.MatchedHardwareIds));
            builder.AppendLine();
            builder.AppendLine("Matching Models sections:");
            builder.AppendLine(result.MatchingModelSections.Count == 0
                ? "لا يوجد"
                : string.Join(Environment.NewLine, result.MatchingModelSections));
            builder.AppendLine();
            builder.AppendLine("Explanation: " + result.Explanation);
            builder.AppendLine("Limitation: " + result.Limitation);
            _report.Text = builder.ToString();
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تحليل INF: " + ex.Message, false);
            _report.Clear();
        }
    }

    private async Task VerifyCatalogMembershipAsync()
    {
        if (string.IsNullOrWhiteSpace(_catalogPath.Text) ||
            string.IsNullOrWhiteSpace(_infPath.Text))
        {
            SetStatus("اختر ملف CAT وملف INF من الحزمة نفسها أولًا.", false);
            return;
        }

        try
        {
            SetStatus("جارٍ التحقق من توقيع CAT وعضوية INF بواسطة Microsoft SignTool...", null);
            var result = await _signatureService.VerifyCatalogMembershipAsync(
                _catalogPath.Text, _infPath.Text);
            SetStatus(result.StatusMessage, result.IsVerified);

            _report.Text = string.Join(Environment.NewLine,
                "Catalog: " + result.CatalogPath,
                "INF member: " + result.MemberPath,
                "Signature + membership accepted: " + result.IsVerified,
                "SignTool: " + (string.IsNullOrWhiteSpace(result.ToolPath) ? "غير متاح" : result.ToolPath),
                "Exit code: " + (result.ExitCode?.ToString() ?? "غير متاح"),
                "",
                "STDOUT:",
                result.StandardOutput,
                "",
                "STDERR:",
                result.StandardError,
                "",
                result.StatusMessage);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر التحقق من CAT وعضوية INF: " + ex.Message, false);
            _report.Clear();
        }
    }

    private void BrowseInf()
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار ملف INF الأصلي من مجلد حزمة OEM",
            Filter = "Windows driver INF (*.inf)|*.inf",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            _infPath.Text = dialog.FileName;
            _report.Clear();
            SetStatus("تم اختيار INF. لم يبدأ تحليل التوافق بعد.", null);
        }
    }

    private void BrowseCatalog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار ملف CAT الذي يأتي داخل حزمة OEM نفسها",
            Filter = "Windows catalog (*.cat)|*.cat",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            _catalogPath.Text = dialog.FileName;
            _report.Clear();
            SetStatus("تم اختيار CAT. تحقق من أن INF وCAT من الحزمة الأصلية نفسها.", null);
        }
    }

    private void SetStatus(string message, bool? success)
    {
        _status.Text = message;
        _status.Foreground = Brush(success switch
        {
            true => "#7DDBAA",
            false => "#FFB4A6",
            _ => "#9FC7E8"
        });
    }

    private static Border Panel(string title, UIElement content)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(title, "#7DD3FC", 10));
        stack.Children.Add(content);
        return new Border
        {
            Background = Brush("#10243A"),
            BorderBrush = Brush("#2D4A68"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 8),
            Child = stack
        };
    }

    private static void StylePathBox(TextBox textBox)
    {
        textBox.IsReadOnly = true;
        textBox.Padding = new Thickness(8);
        textBox.Background = Brush("#0F1E32");
        textBox.Foreground = Brush("#EAF3FF");
        textBox.BorderBrush = Brush("#36516D");
    }

    private static Button PathButton(string label, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 0),
            Background = Brush("#20364F"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096")
        };
        button.Click += handler;
        return button;
    }

    private static Button Button(string label, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 5, 8, 5),
            Background = Brush("#14517A"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096")
        };
        button.Click += handler;
        return button;
    }

    private static TextBlock Text(string value, string color, double size)
        => new()
        {
            Text = value,
            Foreground = Brush(color),
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        };

    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value)!);
}
