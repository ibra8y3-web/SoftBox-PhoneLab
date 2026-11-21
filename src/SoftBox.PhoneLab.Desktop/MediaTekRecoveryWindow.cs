using SoftBox.PhoneLab.Core.Diagnostics;
using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Firmware;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

public sealed class MediaTekRecoveryWindow : Window
{
    private readonly string? _modelNumber;
    private readonly string? _hardwareSku;
    private readonly TextBox _scatter = new();
    private readonly TextBox _da = new();
    private readonly TextBox _auth = new();
    private readonly TextBox _tool = new();
    private readonly TextBox _typed = new();
    private readonly TextBox _report = new();
    private readonly TextBlock _status = new();
    private readonly CheckBox _oemConfirmed = new() { Content = "Scatter وDA وAuth من مصدر OEM مخصص للموديل واللوحة" };
    private readonly CheckBox _authorizationConfirmed = new() { Content = "أمتلك تفويض OEM ولن أتجاوز DAA/SLA أو أي حماية" };
    private readonly Button _inspect;
    private readonly Button _launch;
    private MediaTekScatterInspectionReport? _inspection;
    private string? _daHash;
    private string? _authHash;

    public MediaTekRecoveryWindow(string? modelNumber, string? hardwareSku)
    {
        _modelNumber = modelNumber;
        _hardwareSku = hardwareSku;
        Title = "SoftBox | MediaTek BROM / Preloader Service";
        Width = 1080;
        Height = 820;
        MinWidth = 880;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        _inspect = ButtonControl("فحص Scatter والصور والهاش", "#14517A", Inspect_Click);
        _launch = ButtonControl("فتح SP Flash Tool", "#704B20", Launch_Click);
        _launch.IsEnabled = false;
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock { Text = "MediaTek BROM / Preloader Recovery", FontSize = 23, FontWeight = FontWeights.Bold, Foreground = Brush("#7DD3FC") });
        header.Children.Add(new TextBlock
        {
            Text = "SCATTER VALIDATION • DOWNLOAD AGENT HASH • AUTH FILE • OFFICIAL TOOL HAND-OFF",
            Foreground = Brush("#8199B5"), FontSize = 10, Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "لا يوجد Download Agent عام موثوق لهواتف MediaTek. تفحص هذه الواجهة ملفات الحزمة الفعلية وتفتح SP Flash Tool الذي يقدمه الفني. لا تتجاوز DAA/SLA ولا تنفذ BROM أو التفليش تلقائيًا. أدوات Genio ليست مرجع توافق لهواتف MediaTek.",
            Foreground = Brush("#F0C9A5"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var content = new StackPanel();
        content.Children.Add(Field("MediaTek scatter file", _scatter, "اختيار Scatter", BrowseScatter));
        content.Children.Add(Field("OEM Download Agent binary", _da, "اختيار DA", BrowseDa));
        content.Children.Add(Field("OEM SLA/Auth file", _auth, "اختيار Auth", BrowseAuth));
        content.Children.Add(Field("SP Flash Tool executable supplied by OEM", _tool, "اختيار SP Flash Tool", BrowseTool));
        content.Children.Add(new TextBlock
        {
            Text = "الجهاز المرجعي: Model=" + (_modelNumber ?? "Unknown") + " | Hardware SKU=" + (_hardwareSku ?? "Unknown"),
            Foreground = Brush("#B7C9DE"), Margin = new Thickness(5, 8, 5, 4)
        });
        foreach (var check in new[] { _oemConfirmed, _authorizationConfirmed })
        {
            check.Margin = new Thickness(5);
            check.Foreground = Brush("#EAF3FF");
            content.Children.Add(check);
        }
        content.Children.Add(new TextBlock { Text = "اكتب OPEN SPFLASH بعد نجاح الفحص لفتح الأداة الخارجية.", Foreground = Brush("#F0C9A5"), Margin = new Thickness(5, 7, 5, 3) });
        ApplyInputStyle(_typed);
        content.Children.Add(_typed);

        var actions = new WrapPanel();
        actions.Children.Add(_inspect);
        actions.Children.Add(_launch);
        actions.Children.Add(ButtonControl("مراجع MediaTek الرسمية", "#243B56",
            (_, _) => new OemOfficialToolsWindow(OemServiceVendor.MediaTek) { Owner = this }.Show()));
        content.Children.Add(actions);

        _report.IsReadOnly = true;
        _report.AcceptsReturn = true;
        _report.TextWrapping = TextWrapping.Wrap;
        _report.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _report.Background = Brush("#071625");
        _report.Foreground = Brush("#DCEBFA");
        _report.BorderBrush = Brush("#263D58");
        _report.Padding = new Thickness(10);
        _report.FontFamily = new FontFamily("Consolas");
        _report.FontSize = 10;
        _report.MinHeight = 260;
        _report.Text = "No MediaTek package was inspected.";
        content.Children.Add(_report);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        _status.Text = "اختر scatter وDA وAuth وSP Flash Tool، ثم افحص الحزمة.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private UIElement Field(string label, TextBox box, string buttonText, RoutedEventHandler handler)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("#B7C9DE") });
        var row = new DockPanel();
        var button = ButtonControl(buttonText, "#243B56", handler);
        DockPanel.SetDock(button, Dock.Left);
        row.Children.Add(button);
        ApplyInputStyle(box);
        row.Children.Add(box);
        panel.Children.Add(row);
        return panel;
    }

    private static void ApplyInputStyle(TextBox box)
    {
        box.Margin = new Thickness(4);
        box.Padding = new Thickness(7);
        box.Background = Brush("#071625");
        box.Foreground = Brush("#DCEBFA");
        box.BorderBrush = Brush("#263D58");
    }

    private void BrowseScatter(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Scatter (*.txt)|*.txt|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _scatter.Text = dialog.FileName;
        ResetInspection();
    }

    private void BrowseDa(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Download Agent (*.bin;*.da;*.img)|*.bin;*.da;*.img|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _da.Text = dialog.FileName;
        ResetInspection();
    }

    private void BrowseAuth(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "OEM authentication (*.auth;*.xml;*.sig)|*.auth;*.xml;*.sig|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _auth.Text = dialog.FileName;
        ResetInspection();
    }

    private void BrowseTool(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "SP Flash Tool (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _tool.Text = dialog.FileName;
        _launch.IsEnabled = false;
    }

    private void ResetInspection()
    {
        _inspection = null;
        _daHash = null;
        _authHash = null;
        _launch.IsEnabled = false;
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e)
    {
        _launch.IsEnabled = false;
        try
        {
            var scatter = Required(_scatter.Text, "Scatter");
            var da = Required(_da.Text, "Download Agent");
            var auth = Required(_auth.Text, "OEM Auth");
            if (!File.Exists(da) || !File.Exists(auth))
                throw new FileNotFoundException("DA or Auth file is missing.");
            var daInfo = new FileInfo(da);
            var authInfo = new FileInfo(auth);
            if (daInfo.Length is <= 0 or > 256L * 1024 * 1024 || authInfo.Length is <= 0 or > 64L * 1024 * 1024)
                throw new InvalidDataException("DA/Auth size is outside the inspection limits.");

            _inspection = await MediaTekScatterPackageInspector.InspectAsync(scatter);
            _daHash = await Sha256Async(da);
            _authHash = await Sha256Async(auth);
            var output = new StringBuilder();
            output.AppendLine("MEDIATEK SCATTER PACKAGE PREFLIGHT");
            output.AppendLine("SCATTER SHA256: " + _inspection.ScatterSha256);
            output.AppendLine("PLATFORM: " + _inspection.Platform);
            output.AppendLine("PROJECT: " + _inspection.Project);
            output.AppendLine("PARTITIONS: " + _inspection.Partitions.Count);
            output.AppendLine("IMAGE TOTAL: " + _inspection.TotalImageBytes);
            output.AppendLine("DA: " + daInfo.Name + " | " + daInfo.Length + " | SHA256 " + _daHash);
            output.AppendLine("AUTH: " + authInfo.Name + " | " + authInfo.Length + " | SHA256 " + _authHash);
            output.AppendLine("MODEL REFERENCE: " + (_modelNumber ?? "Unknown"));
            output.AppendLine("HARDWARE SKU REFERENCE: " + (_hardwareSku ?? "Unknown"));
            foreach (var partition in _inspection.Partitions)
                output.AppendLine(partition.Name + " | " + partition.Region + " | " + partition.PartitionType + " | " + partition.FileName + " | " + partition.ImageSizeBytes + " bytes");
            foreach (var missing in _inspection.MissingImages)
                output.AppendLine("MISSING IMAGE: " + missing);
            foreach (var warning in _inspection.Warnings)
                output.AppendLine("WARNING: " + warning);
            output.AppendLine();
            output.AppendLine("STRUCTURAL VALIDITY: " + _inspection.IsStructurallyValid);
            output.AppendLine("DA/Auth signer and chipset authorization have NOT been cryptographically verified by SoftBox.");
            output.AppendLine("The BROM/Preloader write path remains with the external SP Flash Tool; no bypass mode is offered.");
            _report.Text = output.ToString();
            _launch.IsEnabled = _inspection.IsStructurallyValid && File.Exists(_tool.Text);
            _status.Text = "اكتمل فحص scatter والصور. لم يتم فتح BROM أو إرسال كتابة للهاتف.";
        }
        catch (Exception ex)
        {
            ResetInspection();
            _report.Text = ex.ToString();
            _status.Text = "فشل فحص MediaTek: " + ex.Message;
        }
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_inspection is null || !_inspection.IsStructurallyValid)
                throw new InvalidOperationException("Inspect the scatter package first.");
            if (!IsKnownIdentity(_modelNumber) || !IsKnownIdentity(_hardwareSku))
                throw new UnauthorizedAccessException("Exact Model Number and Hardware SKU are required before opening MediaTek recovery tools.");
            if (!_oemConfirmed.IsChecked.GetValueOrDefault() || !_authorizationConfirmed.IsChecked.GetValueOrDefault())
                throw new UnauthorizedAccessException("Both OEM package and authorization confirmations are required.");
            if (!string.Equals(_typed.Text.Trim(), "OPEN SPFLASH", StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Typed confirmation must be OPEN SPFLASH.");

            var da = Required(_da.Text, "Download Agent");
            var auth = Required(_auth.Text, "OEM Auth");
            if (!string.Equals(await Sha256Async(da), _daHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(await Sha256Async(auth), _authHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DA/Auth files changed after preflight. Inspect the package again.");
            var refreshed = await MediaTekScatterPackageInspector.InspectAsync(_inspection.ScatterFile);
            if (!refreshed.IsStructurallyValid ||
                !string.Equals(refreshed.ScatterSha256, _inspection.ScatterSha256, StringComparison.OrdinalIgnoreCase) ||
                refreshed.Partitions.Count != _inspection.Partitions.Count ||
                refreshed.Partitions.Where((partition, index) =>
                    !string.Equals(partition.ImageSha256, _inspection.Partitions[index].ImageSha256, StringComparison.OrdinalIgnoreCase)).Any())
                throw new InvalidDataException("Scatter or one of its images changed after preflight. Inspect the package again.");
            var tool = Required(_tool.Text, "SP Flash Tool executable");
            if (!File.Exists(tool) || !Path.GetExtension(tool).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("SP Flash Tool executable is invalid.", tool);

            var consent = MessageBox.Show(this,
                "سيتم فتح SP Flash Tool الذي اخترته. لم يثبت SoftBox أن DA/Auth صالحان أو موقعان لهذا الجهاز، ولن يختار Download أو يضغطه تلقائيًا. أكمل فقط وفق إجراء OEM الخاص بالموديل.\n\nهل تريد فتح الأداة؟",
                "تأكيد فتح SP Flash Tool", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (consent != MessageBoxResult.Yes) return;
            Process.Start(new ProcessStartInfo(Path.GetFullPath(tool))
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(tool))!
            });
            _status.Text = "تم فتح SP Flash Tool. أكمل وفق تعليمات OEM المخصصة لهذا الموديل واللوحة.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح SP Flash Tool: " + ex.Message;
        }
    }

    private static bool IsKnownIdentity(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           !new[] { "غير متاح", "unknown", "n/a", "not available" }
               .Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException(name + " is required.") : value.Trim();

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label, Padding = new Thickness(11, 7, 11, 7), Margin = new Thickness(3),
            Background = Brush(color), Foreground = Brushes.White, BorderBrush = Brush("#3E7096")
        };
        button.Click += handler;
        return button;
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
