using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

public sealed class DriverPackageManagerWindow : Window
{
    private readonly IReadOnlyList<string> _hardwareIds;
    private readonly string? _manufacturer;
    private readonly string? _model;
    private readonly WindowsDriverPackageManagerService _service = new();
    private readonly Func<string, Task<bool>> _authorizeAndAudit;
    private readonly TextBox _infPath = new();
    private readonly TextBox _catPath = new();
    private readonly TextBox _publishedName = new();
    private readonly TextBox _backupFolder = new();
    private readonly TextBox _report = new();
    private readonly TextBlock _status = new();
    private readonly Button _verifyButton;
    private readonly Button _installButton;
    private DriverPackageInstallValidation? _verified;

    public DriverPackageManagerWindow(
        IReadOnlyList<string> hardwareIds,
        string? manufacturer,
        string? model,
        Func<string, Task<bool>> authorizeAndAudit)
    {
        _hardwareIds = hardwareIds?.Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(128).ToArray()
            ?? Array.Empty<string>();
        _manufacturer = manufacturer;
        _model = model;
        _authorizeAndAudit = authorizeAndAudit ?? throw new ArgumentNullException(nameof(authorizeAndAudit));

        Title = "SoftBox | Windows Driver Package Manager";
        Width = 1040;
        Height = 780;
        MinWidth = 800;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(Text("إدارة حزم تعريفات Windows", 23, "#7DD3FC", true));
        header.Children.Add(Text(
            "الجهاز: " + (_manufacturer ?? "غير معروف") + " • " + (_model ?? "الموديل غير محدد") +
            Environment.NewLine + "Hardware IDs: " + (_hardwareIds.Count == 0 ? "غير متاحة" : string.Join(" | ", _hardwareIds.Take(8))),
            11, "#AFC7DF"));
        header.Children.Add(Text(
            "التثبيت يتطلب تطابق INF مع Hardware IDs وWindows، وتوقيع CAT موثوقًا وعضوية INF داخل CAT. الإزالة تطلب موافقة مسؤول Windows ولا تستخدم /force.",
            11, "#EBCB9B"));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#9FC7E8");
        footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new StackPanel();
        body.Children.Add(FilePickerRow("ملف INF", _infPath, "اختيار INF", BrowseInf));
        body.Children.Add(FilePickerRow("ملف CAT", _catPath, "اختيار CAT", BrowseCat));
        var verifyRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 10) };
        _verifyButton = Button("فحص التوافق والتوقيع وعضوية CAT", "#14517A", Verify_Click);
        _installButton = Button("تثبيت التعريف المحدد", "#176044", Install_Click);
        _installButton.IsEnabled = false;
        verifyRow.Children.Add(_verifyButton);
        verifyRow.Children.Add(_installButton);
        verifyRow.Children.Add(Button("عرض التعريفات المثبتة", "#243B56", ListInstalled_Click));
        body.Children.Add(verifyRow);

        var management = new Border
        {
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var managementPanel = new StackPanel();
        managementPanel.Children.Add(Text("تصدير نسخة احتياطية أو إزالة حزمة منشورة", 14, "#7DD3FC", true));
        managementPanel.Children.Add(Text("اكتب الاسم كما يظهر في pnputil /enum-drivers، مثل oem42.inf. لا يقبل البرنامج مسارًا أو أمرًا إضافيًا.", 10, "#B7C9DE"));
        _publishedName.Margin = new Thickness(0, 7, 0, 5);
        _publishedName.Padding = new Thickness(8);
        _publishedName.Background = Brush("#071625");
        _publishedName.Foreground = Brush("#DCEBFA");
        _publishedName.BorderBrush = Brush("#263D58");
        _publishedName.MaxLength = 32;
        _publishedName.FlowDirection = FlowDirection.LeftToRight;
        managementPanel.Children.Add(_publishedName);

        managementPanel.Children.Add(Text("مجلد النسخ الاحتياطي (مسار محلي):", 10, "#9FC7E8"));
        _backupFolder.Margin = new Thickness(0, 5, 0, 5);
        _backupFolder.Padding = new Thickness(8);
        _backupFolder.Background = Brush("#071625");
        _backupFolder.Foreground = Brush("#DCEBFA");
        _backupFolder.BorderBrush = Brush("#263D58");
        managementPanel.Children.Add(_backupFolder);
        var actions = new WrapPanel();
        actions.Children.Add(Button("تصدير نسخة الحزمة", "#14517A", Export_Click));
        actions.Children.Add(Button("إزالة الحزمة من Windows", "#713B43", Remove_Click));
        managementPanel.Children.Add(actions);
        management.Child = managementPanel;
        body.Children.Add(management);

        _report.IsReadOnly = true;
        _report.AcceptsReturn = true;
        _report.TextWrapping = TextWrapping.Wrap;
        _report.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _report.MinHeight = 230;
        _report.Background = Brush("#071625");
        _report.Foreground = Brush("#DCEBFA");
        _report.BorderBrush = Brush("#263D58");
        _report.Padding = new Thickness(10);
        _report.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        body.Children.Add(_report);

        root.Children.Add(new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        Content = root;
        _status.Text = "لم يتم فحص أي حزمة تعريف حتى الآن.";
    }

    private UIElement FilePickerRow(string label, TextBox target, string buttonText, RoutedEventHandler handler)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        panel.Children.Add(Text(label, 11, "#7DD3FC", true));
        var row = new DockPanel();
        var browse = Button(buttonText, "#243B56", handler);
        DockPanel.SetDock(browse, Dock.Left);
        row.Children.Add(browse);
        target.IsReadOnly = true;
        target.Margin = new Thickness(0, 0, 6, 0);
        target.Padding = new Thickness(8);
        target.Background = Brush("#071625");
        target.Foreground = Brush("#DCEBFA");
        target.BorderBrush = Brush("#263D58");
        row.Children.Add(target);
        panel.Children.Add(row);
        return panel;
    }

    private void BrowseInf(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Windows driver INF (*.inf)|*.inf", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            _infPath.Text = Path.GetFullPath(dialog.FileName);
            ResetValidation();
        }
    }

    private void BrowseCat(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Windows catalog (*.cat)|*.cat", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            _catPath.Text = Path.GetFullPath(dialog.FileName);
            ResetValidation();
        }
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await _authorizeAndAudit("InspectUsbDriverPackage")) return;
            _verifyButton.IsEnabled = false;
            _installButton.IsEnabled = false;
            _status.Text = "جارٍ فحص Hardware IDs وإصدار Windows وتوقيع CAT وعضوية INF...";
            _verified = await _service.ValidateInstallCandidateAsync(_infPath.Text, _catPath.Text, _hardwareIds);
            _installButton.IsEnabled = _verified.IsReady;
            _report.Text = FormatValidation(_verified);
            _status.Text = _verified.IsReady
                ? "اجتازت الحزمة فحوص التوافق والتوقيع. لم يبدأ التثبيت بعد."
                : "تم منع التثبيت. راجع أسباب الرفض في التقرير.";
        }
        catch (Exception ex)
        {
            ResetValidation();
            _status.Text = "تعذر التحقق من الحزمة: " + ex.Message;
        }
        finally
        {
            _verifyButton.IsEnabled = true;
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_verified is not { IsReady: true })
        {
            _status.Text = "يجب فحص الحزمة بنجاح قبل التثبيت.";
            return;
        }
        if (!await _authorizeAndAudit("InstallUsbDriverPackage")) return;
        var answer = MessageBox.Show(this,
            "سيطلب Windows صلاحيات المسؤول لإضافة التعريف وتثبيته على الأجهزة المطابقة. تأكد من المصدر والنسخة الاحتياطية. هل تتابع؟",
            "تأكيد تثبيت تعريف Windows", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            _installButton.IsEnabled = false;
            var result = await _service.InstallVerifiedAsync(_infPath.Text, _catPath.Text, _hardwareIds);
            _status.Text = result.Message + " • Exit code: " + (result.ExitCode?.ToString() ?? "غير متاح");
            _report.AppendText(Environment.NewLine + Environment.NewLine + "INSTALL: " + result.Message);
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تثبيت التعريف: " + ex.Message;
        }
        finally
        {
            _installButton.IsEnabled = _verified?.IsReady == true;
        }
    }

    private async void ListInstalled_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await _authorizeAndAudit("ListUsbDriverPackages")) return;
            _status.Text = "جارٍ قراءة قائمة تعريفات Windows بواسطة pnputil /enum-drivers...";
            var result = await _service.ListInstalledPackagesAsync();
            _report.Text = "EXECUTABLE: " + result.ExecutablePath +
                Environment.NewLine + "EXIT: " + result.ExitCode +
                Environment.NewLine + Environment.NewLine +
                result.StandardOutput + Environment.NewLine + result.StandardError;
            _status.Text = result.ExitCode == 0
                ? "تم تحميل قائمة التعريفات من Windows. استخدم اسم Published Name كما يظهر في التقرير."
                : "فشل pnputil /enum-drivers؛ راجع التقرير.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر قراءة التعريفات المثبتة: " + ex.Message;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await _authorizeAndAudit("ExportUsbDriverPackage")) return;
            var result = await _service.ExportInstalledPackageAsync(_publishedName.Text, _backupFolder.Text);
            _status.Text = result.Message + " • Exit code: " + (result.ExitCode?.ToString() ?? "غير متاح");
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تصدير نسخة التعريف: " + ex.Message;
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (!WindowsDriverPackageManagerService.IsValidPublishedInfName(_publishedName.Text))
        {
            _status.Text = "اكتب اسم منشورًا صالحًا مثل oem42.inf.";
            return;
        }
        var answer = MessageBox.Show(this,
            "إزالة حزمة Windows Driver Store قد تفصل أجهزة تعتمد عليها. سيحاول Windows إلغاء ربطها بالأجهزة، ولن يستخدم SoftBox خيار /force. هل تؤكد إزالة " + _publishedName.Text.Trim() + "؟",
            "تأكيد إزالة تعريف", MessageBoxButton.YesNo, MessageBoxImage.Stop);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            if (!await _authorizeAndAudit("RemoveUsbDriverPackage")) return;
            var result = await _service.RemoveInstalledPackageAsync(_publishedName.Text);
            _status.Text = result.Message + " • Exit code: " + (result.ExitCode?.ToString() ?? "غير متاح");
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر إزالة التعريف: " + ex.Message;
        }
    }

    private void ResetValidation()
    {
        _verified = null;
        _installButton.IsEnabled = false;
        _report.Clear();
    }

    private static string FormatValidation(DriverPackageInstallValidation result)
    {
        var lines = new List<string>
        {
            "DRIVER PACKAGE VALIDATION",
            "INF: " + result.InfPath,
            "CAT: " + result.CatalogPath,
            "READY: " + result.IsReady
        };
        if (result.Compatibility is not null)
        {
            lines.Add("INF compatibility: " + result.Compatibility.Status);
            lines.Add("Provider: " + result.Compatibility.Provider);
            lines.Add("DriverVer: " + result.Compatibility.DriverVersion);
            lines.Add("Windows: " + result.Compatibility.WindowsTarget);
            lines.Add("Matched Hardware IDs: " + string.Join(" | ", result.Compatibility.MatchedHardwareIds));
        }
        if (result.CatalogSignature is not null)
            lines.Add("CAT signature trusted: " + result.CatalogSignature.IsTrusted + " • " + result.CatalogSignature.TrustStatusText);
        if (result.CatalogMembership is not null)
            lines.Add("INF membership verified: " + result.CatalogMembership.IsVerified + " • " + result.CatalogMembership.StatusMessage);
        lines.AddRange(result.Blockers.Select(blocker => "BLOCKED: " + blocker));
        lines.Add("Note: a successful PnPUtil command still requires a fresh PnP/device health check.");
        return string.Join(Environment.NewLine, lines);
    }

    private static TextBlock Text(string value, double size, string color, bool bold = false)
        => new()
        {
            Text = value,
            FontSize = size,
            Foreground = Brush(color),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        };

    private static Button Button(string text, string color, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 7),
            Background = Brush(color),
            Foreground = Brushes.White,
            BorderBrush = Brush("#35516F")
        };
        button.Click += click;
        return button;
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
