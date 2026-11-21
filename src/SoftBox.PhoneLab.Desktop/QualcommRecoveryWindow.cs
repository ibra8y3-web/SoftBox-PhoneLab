using System.Threading.Tasks;
using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Services;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

public sealed class QualcommRecoveryWindow : Window
{
    private readonly DeviceSnapshot? _snapshot;
    private readonly WindowsUsbInventoryService _usb = new();
    private readonly TextBox _qdl = new();
    private readonly TextBox _programmer = new();
    private readonly TextBox _raw = new();
    private readonly TextBox _patch = new();
    private readonly TextBox _serial = new();
    private readonly TextBox _chip = new();
    private readonly ComboBox _storage = new();
    private readonly TextBox _typed = new();
    private readonly TextBox _log = new();
    private readonly TextBlock _status = new();
    private readonly CheckBox _oemPackage = new() { Content = "Programmer وXML من مصدر OEM ومخصصان للموديل واللوحة" };
    private readonly CheckBox _oemAuthorization = new() { Content = "أمتلك تفويض OEM ولن أتجاوز DAA أو Secure Boot أو Authentication" };
    private readonly CheckBox _backup = new() { Content = "لدي نسخة احتياطية وأقبل مخاطر فقد البيانات" };
    private readonly Button _list;
    private readonly Button _chipInfo;
    private readonly Button _inspect;
    private readonly Button _dryRun;
    private readonly Button _flash;
    private QualcommQdlRunner? _runner;
    private QualcommQdlPackageReport? _package;
    private string _chipInfoText = "";
    private QualcommQdlCommandResult? _dryRunResult;
    private bool _busy;

    public QualcommRecoveryWindow(DeviceSnapshot? snapshot)
    {
        _snapshot = snapshot;
        Title = "SoftBox | Qualcomm QDL Recovery";
        Width = 1080;
        Height = 830;
        MinWidth = 900;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        _list = ButtonControl("فحص 9008 و QDL list", "#14517A", async (_, _) => await ListAsync());
        _chipInfo = ButtonControl("قراءة Chip Info", "#14517A", async (_, _) => await ChipInfoAsync());
        _inspect = ButtonControl("فحص الحزمة", "#14517A", async (_, _) => await InspectAsync());
        _dryRun = ButtonControl("Dry-Run", "#704B20", async (_, _) => await DryRunAsync());
        _flash = ButtonControl("بدء الاستعادة الفعلية", "#713B43", async (_, _) => await FlashAsync());
        _chipInfo.IsEnabled = _dryRun.IsEnabled = _flash.IsEnabled = false;
        Content = BuildUi(snapshot);
    }

    private UIElement BuildUi(DeviceSnapshot? snapshot)
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Qualcomm Emergency Download / QDL", FontSize = 23, FontWeight = FontWeights.Bold, Foreground = Brush("#7DD3FC") });
        header.Children.Add(new TextBlock
        {
            Text = "QDL CLI • 05C6:9008 • Firehose programmer • rawprogram / patch XML • mandatory dry-run",
            Foreground = Brush("#F0C9A5"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var content = new StackPanel();
        content.Children.Add(Field("qdl.exe built from Qualcomm QDL source", _qdl, "اختيار", BrowseQdl));
        content.Children.Add(Field("Firehose programmer (.elf / .mbn)", _programmer, "اختيار", BrowseProgrammer));
        content.Children.Add(Field("rawprogram XML files (separate by |)", _raw, "اختيار", (_, _) => BrowseXml(true)));
        content.Children.Add(Field("patch XML files (separate by |; optional)", _patch, "اختيار", (_, _) => BrowseXml(false)));
        content.Children.Add(Field("EDL serial returned by QDL list", _serial, "", null));
        content.Children.Add(new TextBlock { Text = "نوع التخزين من مستندات OEM (إجباري، لا يتم تخمينه)", Foreground = Brush("#B7C9DE"), Margin = new Thickness(5, 4, 5, 2) });
        _storage.ItemsSource = new[] { "emmc", "ufs", "nand", "nvme", "spinor" };
        _storage.SelectedIndex = -1;
        _storage.Margin = new Thickness(4);
        _storage.Padding = new Thickness(7);
        _storage.Width = 220;
        _storage.HorizontalAlignment = HorizontalAlignment.Left;
        _storage.Background = Brush("#071625");
        _storage.Foreground = Brush("#DCEBFA");
        _storage.BorderBrush = Brush("#263D58");
        content.Children.Add(_storage);
        content.Children.Add(Field("Exact chipset/MSM identifier expected by OEM", _chip, "", null));
        content.Children.Add(new TextBlock
        {
            Text = snapshot is null ? "No previous Android identity snapshot." :
                "Previous Android identity: " + snapshot.Manufacturer + " " + snapshot.Model + " | " +
                snapshot.ModelNumber + " | " + snapshot.Board + " | " + snapshot.Soc,
            Foreground = Brush("#B7C9DE"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5, 5, 5, 8)
        });
        foreach (var check in new[] { _oemPackage, _oemAuthorization, _backup })
        {
            check.Margin = new Thickness(5);
            check.Foreground = Brush("#EAF3FF");
            content.Children.Add(check);
        }
        content.Children.Add(new TextBlock { Text = "Type FLASH followed by the exact QDL serial before writing.", Foreground = Brush("#F0C9A5"), Margin = new Thickness(5, 8, 5, 3) });
        content.Children.Add(_typed);
        Style(_typed);
        var actions = new WrapPanel();
        actions.Children.Add(_list); actions.Children.Add(_chipInfo); actions.Children.Add(_inspect); actions.Children.Add(_dryRun); actions.Children.Add(_flash);
        actions.Children.Add(ButtonControl("مصادر Qualcomm الرسمية", "#243B56",
            (_, _) => new OemOfficialToolsWindow(OemServiceVendor.Qualcomm) { Owner = this }.Show()));
        content.Children.Add(actions);
        _log.IsReadOnly = true;
        _log.AcceptsReturn = true;
        _log.TextWrapping = TextWrapping.Wrap;
        _log.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _log.Background = Brush("#071625");
        _log.Foreground = Brush("#DCEBFA");
        _log.BorderBrush = Brush("#263D58");
        _log.Padding = new Thickness(10);
        _log.FontFamily = new FontFamily("Consolas");
        _log.FontSize = 10;
        _log.MinHeight = 270;
        _log.Text = "No QDL command has been executed.";
        content.Children.Add(_log);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Text = "Choose QDL and OEM package, then run list, chipinfo, package inspection and dry-run.";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#9FC7E8");
        footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        return root;
    }

    private UIElement Field(string label, TextBox box, string browse, RoutedEventHandler? action)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("#B7C9DE") });
        var row = new DockPanel();
        if (action is not null)
        {
            var button = ButtonControl(browse, "#243B56", action);
            DockPanel.SetDock(button, Dock.Left);
            row.Children.Add(button);
        }
        Style(box);
        row.Children.Add(box);
        panel.Children.Add(row);
        return panel;
    }

    private static void Style(TextBox box)
    {
        box.Margin = new Thickness(4);
        box.Padding = new Thickness(7);
        box.Background = Brush("#071625");
        box.Foreground = Brush("#DCEBFA");
        box.BorderBrush = Brush("#263D58");
    }

    private void BrowseQdl(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "qdl.exe|qdl.exe|Executable (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _qdl.Text = Path.GetFullPath(dialog.FileName);
            _runner = new QualcommQdlRunner(_qdl.Text);
            ResetPackage();
            _status.Text = "تم اختيار qdl.exe. تأكد من وضع DLLs التابعة في نفس مجلد التشغيل.";
        }
        catch (Exception ex)
        {
            _runner = null;
            _status.Text = "ملف QDL غير صالح: " + ex.Message;
        }
    }

    private void BrowseProgrammer(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Firehose (*.elf;*.mbn)|*.elf;*.mbn", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _programmer.Text = dialog.FileName;
        ResetPackage();
    }

    private void BrowseXml(bool rawprogram)
    {
        var dialog = new OpenFileDialog { Filter = "XML (*.xml)|*.xml", CheckFileExists = true, Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        (rawprogram ? _raw : _patch).Text = string.Join("|", dialog.FileNames);
        ResetPackage();
    }

    private void ResetPackage()
    {
        _package = null;
        _dryRunResult = null;
        _chipInfoText = "";
        _dryRun.IsEnabled = false;
        _flash.IsEnabled = false;
    }

    private async Task ListAsync()
    {
        await RunUiAsync("Checking Windows PnP 9008 and qdl list ...", async () =>
        {
            var runner = _runner ?? throw new FileNotFoundException("Select qdl.exe first.");
            var pnp = await _usb.EnumeratePresentDevicesAsync();
            var devices = pnp.Where(item => item.VendorId.Equals("05C6", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (devices.Length == 0)
                throw new InvalidOperationException("Windows has no present Qualcomm VID 05C6 device. Qualcomm QDL supports EDL USB product IDs beyond the common 9008 value.");
            var result = await _runner!.ListDevicesAsync();
            _log.Text = string.Join(Environment.NewLine, devices.Select(item => item.FriendlyName + " | " + item.InstanceId)) +
                Environment.NewLine + Format("QDL LIST", result);
            if (!result.Success) throw new InvalidOperationException("qdl list failed. Verify the QDLoader driver and selected QDL backend.");
            _chipInfo.IsEnabled = true;
            return "QDL list succeeded. Enter the serial for the specific EDL handset.";
        });
    }

    private async Task ChipInfoAsync()
    {
        await RunUiAsync("Reading chip info ...", async () =>
        {
            EnsureRunner();
            var serial = Required(_serial.Text, "EDL serial");
            var list = await _runner!.ListDevicesAsync();
            if (!list.Success || !ContainsSerial(list.CombinedOutput, serial))
                throw new UnauthorizedAccessException("The selected EDL serial is not present in the current QDL device list.");
            var result = await _runner.ReadChipInfoAsync(serial);
            _log.Text = Format("QDL CHIPINFO", result);
            if (!result.Success) throw new InvalidOperationException("QDL chipinfo failed.");
            _chipInfoText = result.CombinedOutput;
            if (string.IsNullOrWhiteSpace(_chip.Text) || !_chipInfoText.Contains(_chip.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Chip Info does not contain the expected OEM chipset identifier. Dry-run is blocked.");
            _dryRun.IsEnabled = _package is not null;
            return "Chip Info matches the expected identifier.";
        });
    }

    private async Task InspectAsync()
    {
        await RunUiAsync("Inspecting programmer, rawprogram, patch XML and images ...", async () =>
        {
            var programmer = Required(_programmer.Text, "Firehose programmer");
            _package = await QualcommQdlPackageInspector.InspectAsync(
                Path.GetDirectoryName(Path.GetFullPath(programmer))!, programmer,
                Split(_raw.Text), Split(_patch.Text));
            _dryRunResult = null;
            _flash.IsEnabled = false;
            _dryRun.IsEnabled = !string.IsNullOrWhiteSpace(_chip.Text) &&
                _chipInfoText.Contains(_chip.Text.Trim(), StringComparison.OrdinalIgnoreCase);
            _log.Text = PackageReport(package);
            return "Package structure, referenced images and SHA-256 values were inspected.";
        });
    }

    private async Task DryRunAsync()
    {
        await RunUiAsync("Running QDL dry-run (no flash writes) ...", async () =>
        {
            EnsureRunner();
            if (_package is null) throw new InvalidOperationException("Inspect the OEM package first.");
            var result = await _runner!.DryRunAsync(_package, Required(_serial.Text, "EDL serial"), SelectedStorageType());
            _log.Text = Format("QDL DRY-RUN", result) + Environment.NewLine + PackageReport(package);
            if (!result.Success) throw new InvalidOperationException("Dry-run returned a non-zero exit code. Flash stays blocked.");
            _dryRunResult = result;
            _flash.IsEnabled = true;
            return "QDL dry-run succeeded. No device data was written.";
        });
    }

    private async Task FlashAsync()
    {
        try
        {
        var serial = Required(_serial.Text, "EDL serial");
        var package = _package ?? throw new InvalidOperationException("Inspect the package first.");
        if (_dryRunResult?.Success != true)
            throw new InvalidOperationException("A successful package inspection and dry-run are mandatory.");
        if (!_oemPackage.IsChecked.GetValueOrDefault() || !_oemAuthorization.IsChecked.GetValueOrDefault() || !_backup.IsChecked.GetValueOrDefault())
            throw new UnauthorizedAccessException("All OEM, authorization and backup confirmations are mandatory.");
        var snapshot = _snapshot;
        if (snapshot is null || !IsKnownIdentity(snapshot.ModelNumber) ||
            !IsKnownIdentity(snapshot.Board) || !IsKnownIdentity(snapshot.Soc))
            throw new UnauthorizedAccessException("A previous exact model number, board identity and SoC snapshot are required before any QDL write.");
        var storageType = SelectedStorageType();
        if (!string.Equals(_typed.Text.Trim(), "FLASH " + snapshot.ModelNumber + " " + serial, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Typed confirmation must be FLASH followed by the exact model number and EDL serial.");

        var answer = MessageBox.Show(this,
            "QDL will write the selected Firehose package to this device. An incorrect programmer or XML may permanently damage it. No security bypass will be attempted.\n\nSerial: " + serial +
            "\nProgrammer SHA256: " + package.ProgrammerSha256 + "\nImages: " + package.Images.Count + "\n\nProceed?",
            "Confirm Qualcomm EDL write", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        await RunUiAsync("QDL flash running. Do not disconnect USB ...", async () =>
        {
            var runner = _runner ?? throw new FileNotFoundException("Select qdl.exe first.");
            var expectedChip = Required(_chip.Text, "Expected OEM chip identifier");
            var currentPackage = await QualcommQdlPackageInspector.InspectAsync(
                package.PackageDirectory, package.ProgrammerPath, package.RawProgramXml, package.PatchXml);
            if (!string.Equals(currentPackage.ProgrammerSha256, package.ProgrammerSha256, StringComparison.OrdinalIgnoreCase) ||
                !currentPackage.RawProgramXml.SequenceEqual(package.RawProgramXml, StringComparer.OrdinalIgnoreCase) ||
                !currentPackage.PatchXml.SequenceEqual(package.PatchXml, StringComparer.OrdinalIgnoreCase) ||
                !currentPackage.Images.SequenceEqual(package.Images) ||
                !currentPackage.XmlFiles.SequenceEqual(package.XmlFiles) ||
                !currentPackage.ProgramEntries.SequenceEqual(package.ProgramEntries))
                throw new InvalidDataException("Programmer, XML or firmware images changed after dry-run. Re-inspect and dry-run the package again.");
            var pnp = await _usb.EnumeratePresentDevicesAsync();
            if (!pnp.Any(item => item.VendorId.Equals("05C6", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Qualcomm EDL USB device disconnected. Write was blocked.");
            var devices = await runner.ListDevicesAsync();
            if (!devices.Success || !ContainsSerial(devices.CombinedOutput, serial))
                throw new UnauthorizedAccessException("The selected EDL serial is no longer present in qdl list.");
            var chipInfo = await runner.ReadChipInfoAsync(serial);
            if (!chipInfo.Success || !chipInfo.CombinedOutput.Contains(expectedChip, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Live Chip Info no longer matches the expected OEM chip identifier. Write was blocked.");
            var result = await runner.FlashAsync(package, serial, storageType);
            _log.Text = Format("QDL FLASH", result) + Environment.NewLine + PackageReport(_package);
            if (!result.Success) throw new InvalidOperationException("QDL flash failed. Preserve the full log and consult OEM instructions.");
            return "QDL reported exit code 0. Verify device boot and hardware functions before further service.";
        });
        }
        catch (Exception ex)
        {
            _status.Text = "لم يتم بدء كتابة QDL: " + ex.Message;
            _log.Text = ex.ToString();
        }
    }

    private async Task RunUiAsync(string message, Func<Task<string>> action)
    {
        if (_busy) return;
        _busy = true;
        SetEnabled(false);
        _status.Text = message;
        try { _status.Text = await action(); }
        catch (Exception ex) { _status.Text = "QDL action failed: " + ex.Message; _log.Text = ex.ToString(); }
        finally { _busy = false; SetEnabled(true); }
    }

    private void SetEnabled(bool enabled)
    {
        _list.IsEnabled = enabled && _runner is not null;
        _chipInfo.IsEnabled = enabled && _runner is not null;
        _inspect.IsEnabled = enabled;
        _dryRun.IsEnabled = enabled && _package is not null && _chipInfoText.Contains(_chip.Text.Trim(), StringComparison.OrdinalIgnoreCase);
        _flash.IsEnabled = enabled && _package is not null && _dryRunResult?.Success == true;
    }

    private static bool IsKnownIdentity(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           !new[] { "غير متاح", "unknown", "n/a", "not available" }
               .Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool ContainsSerial(string output, string serial)
        => !string.IsNullOrWhiteSpace(serial) &&
           Regex.IsMatch(output ?? "", @"(?<![A-Za-z0-9._-])" + Regex.Escape(serial) + @"(?![A-Za-z0-9._-])",
               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private QualcommQdlStorageType SelectedStorageType()
    {
        var value = _storage.SelectedItem as string;
        if (!Enum.TryParse<QualcommQdlStorageType>(value, ignoreCase: true, out var storage))
            throw new InvalidOperationException("Select the exact OEM storage type before continuing.");
        return storage;
    }

    private void EnsureRunner()
    {
        if (_runner is null || !File.Exists(_runner.ExecutablePath))
            throw new FileNotFoundException("Select a valid qdl.exe built from Qualcomm's official QDL source repository.");
    }

    private static string[] Split(string value)
        => value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException(name + " is required.") : value.Trim();

    private static string Format(string title, QualcommQdlCommandResult result)
        => title + Environment.NewLine + "Exit code: " + result.ExitCode + Environment.NewLine +
           "Start: " + result.StartedAtUtc.ToString("O") + Environment.NewLine +
           "End: " + result.CompletedAtUtc.ToString("O") + Environment.NewLine + result.CombinedOutput;

    private static string PackageReport(QualcommQdlPackageReport package)
    {
        var text = new StringBuilder();
        text.AppendLine("FIREHOSE PROGRAMMER: " + package.ProgrammerPath);
        text.AppendLine("PROGRAMMER SHA256: " + package.ProgrammerSha256);
        text.AppendLine("RAWPROGRAM XML: " + string.Join(", ", package.RawProgramXml.Select(Path.GetFileName)));
        text.AppendLine("PATCH XML: " + string.Join(", ", package.PatchXml.Select(Path.GetFileName)));
        text.AppendLine("PROGRAM ENTRIES: " + package.ProgramEntries.Count);
        foreach (var image in package.Images)
            text.AppendLine(image.RelativePath + " | " + image.SizeBytes + " | " + image.Sha256);
        text.AppendLine("XML INPUTS: " + package.XmlFiles.Count);
        foreach (var xml in package.XmlFiles)
            text.AppendLine(xml.RelativePath + " | " + xml.SizeBytes + " | " + xml.Sha256);
        foreach (var warning in package.Warnings)
            text.AppendLine("WARNING: " + warning);
        return text.ToString();
    }

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(3), Background = Brush(color), Foreground = Brushes.White, BorderBrush = Brush("#3E7096") };
        button.Click += handler;
        return button;
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
