using SoftBox.PhoneLab.Core.Diagnostics;
using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Firmware;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Safe hand-off workspace for a user-provided Samsung Odin executable and local firmware slots.
/// SoftBox does not inject firmware into Odin or automate the Start/flash button.
/// </summary>
public sealed class SamsungOdinAssistantWindow : Window
{
    private readonly TextBox _odinExecutable = new();
    private readonly TextBox _inspection = new();
    private readonly TextBlock _status = new();
    private readonly Dictionary<SamsungOdinSlot, TextBox> _slotPaths = [];
    private readonly Dictionary<SamsungOdinSlot, string> _inspectedHashes = [];
    private readonly Button _inspectButton;
    private readonly Button _launchButton;
    private readonly Button _inspectOdinUiButton;
    private readonly Button _startOdinButton;
    private readonly Button _downloadModeButton;
    private readonly CheckBox _confirmCompatibility = new()
    {
        Content = "أؤكد مطابقة موديل الهاتف والمنطقة وBinary/Bootloader وAnti-Rollback"
    };
    private readonly CheckBox _confirmBackup = new()
    {
        Content = "أؤكد وجود نسخة احتياطية صالحة وأنني أتحمل مخاطر التفليش"
    };
    private readonly CheckBox _confirmComPort = new()
    {
        Content = "أؤكد أن منفذ COM الظاهر في Odin يخص نفس الهاتف المحدد في SoftBox"
    };
    private readonly TextBox _flashConfirmation = new();
    private readonly SamsungOdinUiAutomationService _odinAutomation = new();
    private readonly AdbClient? _adb;
    private readonly string? _deviceSerial;
    private readonly string? _deviceManufacturer;
    private readonly string? _deviceModel;
    private bool _packagesInspected;
    private int? _inspectedOdinProcessId;
    private string? _inspectedComPort;

    public SamsungOdinAssistantWindow(
        AdbClient? adb = null,
        string? deviceSerial = null,
        string? deviceManufacturer = null,
        string? deviceModel = null)
    {
        _adb = adb;
        _deviceSerial = AdbClient.IsSafeSerial(deviceSerial ?? "") ? deviceSerial : null;
        _deviceManufacturer = deviceManufacturer;
        _deviceModel = deviceModel;
        if (_deviceSerial is not null &&
            AndroidDeviceToolsService.NormalizeVendor(_deviceManufacturer) != AndroidVendorKind.Samsung)
            throw new UnauthorizedAccessException("تم رفض ربط Odin بجهاز لم يتم تأكيد أنه Samsung.");

        Title = "SoftBox | Samsung Odin Assistant";
        Width = 1120;
        Height = 820;
        MinWidth = 850;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _inspectButton = ButtonControl("فحص حزم Firmware والبصمات", "#14517A", InspectPackages_Click);
        _launchButton = ButtonControl("فتح Odin المحدد", "#176044", LaunchOdin_Click);
        _inspectOdinUiButton = ButtonControl("فحص واجهة Odin ومنفذ COM", "#14517A", InspectOdinUi_Click);
        _startOdinButton = ButtonControl("تنفيذ Start بعد التحقق", "#713B43", StartOdin_Click);
        _downloadModeButton = ButtonControl("إعادة تشغيل Samsung إلى Download Mode", "#704B20", DownloadMode_Click);
        _launchButton.IsEnabled = false;
        _inspectOdinUiButton.IsEnabled = false;
        _startOdinButton.IsEnabled = false;
        _downloadModeButton.IsEnabled = _adb is not null && _deviceSerial is not null;
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Samsung Odin Assistant",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(TextBlock(
            "إدارة حزم BL / AP / CP / CSC، وربط Download Mode، وفحص واجهة Odin عبر Windows UI Automation. لا يستخدم SoftBox نقرات إحداثية أو أوامر غير موثقة.",
            12, "#B7C9DE"));
        header.Children.Add(TextBlock(
            "تنبيه: اختيار ملف أو حساب SHA-256 لا يثبت صحة المصدر أو توافق Firmware مع موديل الهاتف أو المنطقة أو Anti-Rollback. لا تبدأ التفليش إلا بعد مراجعة هذه المعلومات ووجود نسخة احتياطية.",
            11, "#F0C9A5"));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(_status);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(_inspectButton);
        buttons.Children.Add(_launchButton);
        buttons.Children.Add(_downloadModeButton);
        buttons.Children.Add(_inspectOdinUiButton);
        buttons.Children.Add(_startOdinButton);
        buttons.Children.Add(ButtonControl("مصادر Samsung الرسمية", "#243B56",
            (_, _) => new OemOfficialToolsWindow(OemServiceVendor.Samsung) { Owner = this }.Show()));
        footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var content = new StackPanel();
        content.Children.Add(BuildOdinExecutablePanel());
        content.Children.Add(BuildSlotPanel(SamsungOdinSlot.BL, "BL — Bootloader"));
        content.Children.Add(BuildSlotPanel(SamsungOdinSlot.AP, "AP — Firmware / PDA"));
        content.Children.Add(BuildSlotPanel(SamsungOdinSlot.CP, "CP — Modem / Radio"));
        content.Children.Add(BuildSlotPanel(SamsungOdinSlot.CSC, "CSC — تثبيت قد يمسح بيانات المستخدم"));
        content.Children.Add(BuildSlotPanel(SamsungOdinSlot.HomeCsc, "HOME_CSC — يحاول الاحتفاظ ببيانات المستخدم، دون ضمان"));
        content.Children.Add(TextBlock(
            "اختر CSC أو HOME_CSC فقط، ولا تضع الملفين معًا. AP مطلوب في خطة الحزم. فحص الحزمة لا يثبت وحده توافق الموديل أو المنطقة أو Binary أو Anti-Rollback.",
            11, "#F0C9A5"));

        _confirmCompatibility.Foreground = Brush("#F0C9A5");
        _confirmBackup.Foreground = Brush("#F0C9A5");
        _confirmCompatibility.Margin = new Thickness(4);
        _confirmBackup.Margin = new Thickness(4);
        _confirmComPort.Foreground = Brush("#F0C9A5");
        _confirmComPort.Margin = new Thickness(4);
        content.Children.Add(_confirmCompatibility);
        content.Children.Add(_confirmBackup);
        content.Children.Add(_confirmComPort);
        _flashConfirmation.Width = 260;
        _flashConfirmation.Margin = new Thickness(4);
        _flashConfirmation.Padding = new Thickness(8);
        _flashConfirmation.Background = Brush("#071625");
        _flashConfirmation.Foreground = Brush("#DCEBFA");
        _flashConfirmation.BorderBrush = Brush("#263D58");
        _flashConfirmation.ToolTip = "اكتب FLASH ثم آخر أربعة أحرف من Serial للجهاز المحدد";
        content.Children.Add(_flashConfirmation);
        content.Children.Add(TextBlock(
            "زر Start لن يتفعّل إلا إذا استطاع Windows قراءة نافذة Odin وزر Start الوحيد وأسماء كل الحزم ومنفذ COM. عند غياب أي دليل ستُرفض الأتمتة. الضغط على Start لا يثبت نجاح التفليش.",
            11, "#F0C9A5"));

        _inspection.IsReadOnly = true;
        _inspection.AcceptsReturn = true;
        _inspection.TextWrapping = TextWrapping.Wrap;
        _inspection.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _inspection.MinHeight = 170;
        _inspection.Background = Brush("#071625");
        _inspection.Foreground = Brush("#DCEBFA");
        _inspection.BorderBrush = Brush("#263D58");
        _inspection.Padding = new Thickness(10);
        _inspection.FontFamily = new FontFamily("Consolas");
        _inspection.FontSize = 10;
        _inspection.Text = "لم يتم فحص حزم Firmware بعد.";
        content.Children.Add(_inspection);

        root.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });

        _status.Text = "اختر ملف Odin التنفيذي وحزم TAR/TAR.MD5، ثم افحص البصمات قبل فتح الأداة.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        return root;
    }

    private UIElement BuildOdinExecutablePanel()
    {
        var border = Panel();
        var panel = new StackPanel();
        panel.Children.Add(TextBlock("Odin executable", 16, "#7DD3FC"));
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var browse = ButtonControl("اختيار Odin.exe", "#243B56", BrowseOdin_Click);
        DockPanel.SetDock(browse, Dock.Left);
        row.Children.Add(browse);
        _odinExecutable.IsReadOnly = true;
        _odinExecutable.Background = Brush("#071625");
        _odinExecutable.Foreground = Brush("#DCEBFA");
        _odinExecutable.BorderBrush = Brush("#263D58");
        _odinExecutable.Padding = new Thickness(8);
        row.Children.Add(_odinExecutable);
        panel.Children.Add(row);
        panel.Children.Add(TextBlock(
            "اختر نسخة Odin حصلت عليها من مصدر تثق به. SoftBox لا ينزل الأداة أو يتحقق من ناشرها ولا يرفقها مع البرنامج.",
            11, "#B7C9DE"));
        border.Child = panel;
        return border;
    }

    private UIElement BuildSlotPanel(SamsungOdinSlot slot, string label)
    {
        var border = Panel();
        var panel = new StackPanel();
        panel.Children.Add(TextBlock(label, 13, "#7DD3FC"));
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var browse = ButtonControl("اختيار الملف", "#243B56", (_, _) => BrowsePackage(slot));
        DockPanel.SetDock(browse, Dock.Left);
        row.Children.Add(browse);

        var path = new TextBox
        {
            IsReadOnly = true,
            Background = Brush("#071625"),
            Foreground = Brush("#DCEBFA"),
            BorderBrush = Brush("#263D58"),
            Padding = new Thickness(8)
        };
        _slotPaths.Add(slot, path);
        row.Children.Add(path);
        panel.Children.Add(row);
        border.Child = panel;
        return border;
    }

    private void BrowseOdin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار نسخة Odin التي حصلت عليها من مصدر موثوق",
            Filter = "Windows executable (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            _odinExecutable.Text = Path.GetFullPath(dialog.FileName);
            _inspectedOdinProcessId = null;
            _inspectedComPort = null;
            _startOdinButton.IsEnabled = false;
            _inspectOdinUiButton.IsEnabled = _packagesInspected && _deviceSerial is not null;
            _startOdinButton.IsEnabled = false;
            _status.Text = "تم اختيار ملف Odin. لم يتم تشغيله أو التحقق من ناشره.";
        }
    }

    private void BrowsePackage(SamsungOdinSlot slot)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار حزمة Firmware لموضع " + slot,
            Filter = "Samsung Odin TAR packages (*.tar;*.tar.md5)|*.tar;*.tar.md5",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            _slotPaths[slot].Text = Path.GetFullPath(dialog.FileName);
            _packagesInspected = false;
            _inspectedOdinProcessId = null;
            _inspectedComPort = null;
            _inspectedHashes.Clear();
            _launchButton.IsEnabled = false;
            _inspectOdinUiButton.IsEnabled = false;
            _startOdinButton.IsEnabled = false;
            _inspection.Text = "تغير اختيار الملفات. أعد فحص الحزم والبصمات قبل فتح Odin.";
            _status.Text = "تم اختيار ملف " + slot + ".";
        }
    }

    private async void InspectPackages_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _inspectButton.IsEnabled = false;
            _launchButton.IsEnabled = false;
            _inspectOdinUiButton.IsEnabled = false;
            _startOdinButton.IsEnabled = false;
            _packagesInspected = false;
            _inspectedHashes.Clear();

            var entries = _slotPaths
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Text))
                .Select(pair => new SamsungOdinPackageFile(pair.Key, pair.Value.Text))
                .ToArray();
            var plan = SamsungOdinPackagePolicy.BuildPlan(entries);
            var lines = new List<string>
            {
                "SAMSUNG ODIN PACKAGE SELECTION",
                "Inspection: slot policy / TAR structure / appended MD5 / SHA-256",
                "No package is flashed or passed into Odin automatically.",
                ""
            };

            foreach (var item in plan)
            {
                if (!File.Exists(item.Path))
                    throw new FileNotFoundException("ملف Firmware غير موجود.", item.Path);
                var info = new FileInfo(item.Path);
                if (info.Length <= 0 || info.Length > 16L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("حجم ملف Firmware فارغ أو يتجاوز الحد 16 GiB: " + info.Name);

                var tarInspection = await OdinTarMd5Inspector.InspectAsync(item.Path);
                if (!tarInspection.IsValid)
                    throw new InvalidDataException(
                        $"فشل فحص حزمة {item.Slot}: {string.Join(" | ", tarInspection.Errors)}");

                var hash = await CalculateSha256Async(item.Path);
                _inspectedHashes[item.Slot] = hash;
                lines.Add($"{item.Slot}: {info.Name}");
                lines.Add($"  Size: {info.Length:N0} bytes");
                lines.Add($"  SHA-256: {hash}");
                lines.Add($"  TAR readable: {tarInspection.IsTarReadable}");
                lines.Add($"  Appended MD5: {(tarInspection.HasAppendedMd5 ? (tarInspection.AppendedMd5Valid ? "VALID" : "INVALID") : "NOT PRESENT")}");
                lines.Add($"  TAR entries: {tarInspection.EntryNames.Count}");
                lines.Add($"  Partition payloads: {string.Join(", ", tarInspection.PartitionPayloads.Take(20))}");
                foreach (var warning in tarInspection.Warnings)
                    lines.Add("  WARNING: " + warning);
                lines.Add("");
            }

            lines.Add("TAR.MD5 verifies package integrity only; SHA-256 is recorded locally.");
            lines.Add("No publisher authenticity or exact device/region/bootloader compatibility verdict is produced.");
            lines.Add("Odin Start may be invoked only by the separate fail-closed Windows UI Automation workflow after fresh control, COM, package and technician-confirmation checks.");
            _inspection.Text = string.Join(Environment.NewLine, lines);
            _packagesInspected = true;
            _launchButton.IsEnabled = true;
            _inspectOdinUiButton.IsEnabled = !string.IsNullOrWhiteSpace(_odinExecutable.Text) &&
                File.Exists(_odinExecutable.Text) && _deviceSerial is not null;
            _status.Text = $"تم فحص {plan.Count} ملف/ملفات وحساب SHA-256 من البايتات الحالية. أعد فحص واجهة Odin بعد تحميل الملفات يدويًا داخلها.";
        }
        catch (Exception ex)
        {
            _inspection.Text = "تعذر فحص حزم Odin:\n" + ex.Message;
            _status.Text = "فشل فحص الحزم.";
            MessageBox.Show(this, ex.Message, "Samsung Odin Assistant", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _inspectButton.IsEnabled = true;
        }
    }

    private async void LaunchOdin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _odinExecutable.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
                !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("حدد ملف Odin.exe صالحًا أولًا.", path);
            if (!_packagesInspected)
                throw new InvalidOperationException("افحص حزم Firmware أولًا قبل فتح Odin.");

            var entries = _slotPaths
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Text))
                .Select(pair => new SamsungOdinPackageFile(pair.Key, pair.Value.Text))
                .ToArray();
            var plan = SamsungOdinPackagePolicy.BuildPlan(entries);
            foreach (var item in plan)
            {
                if (!File.Exists(item.Path))
                    throw new FileNotFoundException("ملف Firmware غير موجود.", item.Path);
                var currentHash = await CalculateSha256Async(item.Path);
                if (!_inspectedHashes.TryGetValue(item.Slot, out var inspectedHash) ||
                    !CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(currentHash), Convert.FromHexString(inspectedHash)))
                    throw new InvalidDataException("تغير ملف Firmware بعد الفحص. أعد فحص الملفات قبل فتح Odin: " + Path.GetFileName(item.Path));
            }

            var executableHash = await CalculateSha256Async(path);
            var confirmation = MessageBox.Show(this,
                "سيتم فتح البرنامج الخارجي المحدد دون تمرير أوامر أو ملفات تلقائيًا.\n\n" +
                "Executable: " + path + "\nSHA-256: " + executableHash + "\n\n" +
                "لم يتحقق SoftBox من ناشر Odin أو مصدره، ولم يتحقق من توافق ملفات Firmware مع الهاتف. " +
                "تحميل الملفات داخل Odin والضغط على Start مسؤولية الفني. هل تريد فتح البرنامج؟",
                "تأكيد فتح Odin الخارجي", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
                return;

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!,
                UseShellExecute = false
            });
            if (process is null)
                throw new InvalidOperationException("لم يؤكد Windows بدء تشغيل Odin.");

            _status.Text = "تم طلب فتح Odin. حمّل ملفات BL/AP/CP وCSC أو HOME_CSC يدويًا داخل Odin بعد التأكد من الموديل والمنطقة. SoftBox لم يبدأ التفليش.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح Odin: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Samsung Odin Assistant", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DownloadMode_Click(object sender, RoutedEventArgs e)
    {
        if (_adb is null || _deviceSerial is null ||
            AndroidDeviceToolsService.NormalizeVendor(_deviceManufacturer) != AndroidVendorKind.Samsung)
        {
            _status.Text = "لا يوجد هاتف Samsung مصرح به مرتبط بهذه الجلسة.";
            return;
        }

        var answer = MessageBox.Show(this,
            $"سيتم إرسال ADB reboot download إلى الهاتف المحدد:\n{_deviceManufacturer} {_deviceModel}\nSerial: {_deviceSerial}\n\nقد يتوقف ADB ويظهر الهاتف في Odin بعد تثبيت تعريف Samsung USB. متابعة؟",
            "تأكيد Samsung Download Mode", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        _downloadModeButton.IsEnabled = false;
        try
        {
            var service = new AndroidDeviceToolsService(_adb);
            var output = await service.RebootDeviceAsync(_deviceSerial, AndroidRebootTarget.DownloadMode);
            _status.Text = "تم إرسال أمر Download Mode بعد إعادة التحقق من هوية Samsung وتصريح ADB. " + output +
                " انتظر ظهور منفذ COM في Odin ثم حمّل الحزم يدويًا.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر دخول Download Mode: " + ex.Message;
        }
        finally
        {
            _downloadModeButton.IsEnabled = true;
        }
    }

    private void InspectOdinUi_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_packagesInspected)
                throw new InvalidOperationException("افحص حزم Firmware أولًا.");
            if (_deviceSerial is null)
                throw new InvalidOperationException("فحص واجهة Odin قبل Start يتطلب هاتف Samsung مصرحًا ومحددًا في SoftBox.");
            var report = _odinAutomation.Inspect(
                _odinExecutable.Text,
                SelectedPackagePaths(),
                _confirmCompatibility.IsChecked == true,
                _confirmBackup.IsChecked == true,
                _confirmComPort.IsChecked == true);
            _startOdinButton.IsEnabled = report.Decision.CanInvokeStart;
            _inspectedOdinProcessId = report.Decision.CanInvokeStart ? report.ProcessId : null;
            _inspectedComPort = report.Decision.CanInvokeStart ? report.ComPort : null;
            _inspection.AppendText(Environment.NewLine + Environment.NewLine +
                "SAMSUNG ODIN UI AUTOMATION INSPECTION" + Environment.NewLine +
                "Window: " + (report.WindowTitle ?? "Not detected") + Environment.NewLine +
                "Process ID: " + (report.ProcessId?.ToString() ?? "Not detected") + Environment.NewLine +
                "COM Port: " + (report.ComPort ?? "Not detected") + Environment.NewLine +
                "Start allowed: " + report.Decision.CanInvokeStart + Environment.NewLine +
                "Verified evidence:" + Environment.NewLine +
                string.Join(Environment.NewLine, report.Decision.VerifiedEvidence.Select(x => "  + " + x)) +
                Environment.NewLine + "Blockers:" + Environment.NewLine +
                string.Join(Environment.NewLine, report.Decision.Blockers.Select(x => "  - " + x)));
            _status.Text = report.Decision.CanInvokeStart
                ? "اكتملت أدلة واجهة Odin. لا يبدأ التفليش حتى تكتب عبارة التأكيد النهائية."
                : "تم منع Start بسبب عدم اكتمال أدلة Windows UI Automation. راجع أسباب المنع.";
        }
        catch (Exception ex)
        {
            _startOdinButton.IsEnabled = false;
            _inspectedOdinProcessId = null;
            _inspectedComPort = null;
            _status.Text = "تعذر فحص واجهة Odin: " + ex.Message;
        }
    }

    private async void StartOdin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_deviceSerial is null)
                throw new InvalidOperationException("تنفيذ Start يتطلب هاتف Samsung مصرحًا ومحددًا.");
            if (!_packagesInspected)
                throw new InvalidOperationException("أعد فحص حزم Firmware قبل Start.");
            var suffix = _deviceSerial.Length > 4 ? _deviceSerial[^4..] : _deviceSerial;
            if (!string.Equals(_flashConfirmation.Text.Trim(), "FLASH " + suffix, StringComparison.Ordinal))
                throw new InvalidOperationException("اكتب FLASH ثم مسافة وآخر أربعة أحرف من Serial لتأكيد الجهاز المستهدف.");

            if (_confirmCompatibility.IsChecked != true || _confirmBackup.IsChecked != true ||
                _confirmComPort.IsChecked != true)
                throw new InvalidOperationException("يجب تأكيد توافق الموديل والمنطقة وBinary/Anti-Rollback والنسخة الاحتياطية وتطابق الهاتف مع منفذ COM.");

            var paths = SelectedPackagePaths();
            foreach (var item in _slotPaths.Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Text)))
            {
                if (!File.Exists(item.Value.Text))
                    throw new FileNotFoundException("اختفى ملف Firmware بعد الفحص.", item.Value.Text);
                var current = await CalculateSha256Async(item.Value.Text);
                if (!_inspectedHashes.TryGetValue(item.Key, out var inspected) ||
                    !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(current), Convert.FromHexString(inspected)))
                    throw new InvalidDataException("تغيرت حزمة Firmware بعد الفحص: " + Path.GetFileName(item.Value.Text));
            }

            var report = _odinAutomation.Inspect(
                _odinExecutable.Text, paths,
                _confirmCompatibility.IsChecked == true,
                _confirmBackup.IsChecked == true,
                _confirmComPort.IsChecked == true);
            if (!report.Decision.CanInvokeStart)
            {
                _startOdinButton.IsEnabled = false;
                throw new InvalidOperationException("لم تعد أدلة Odin صالحة؛ تم منع Start:\n" +
                    string.Join("\n", report.Decision.Blockers));
            }
            if (_inspectedOdinProcessId is null || _inspectedComPort is null ||
                report.ProcessId != _inspectedOdinProcessId ||
                !string.Equals(report.ComPort, _inspectedComPort, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("تغيرت عملية Odin أو منفذ COM بعد آخر فحص. أعد الفحص وتأكد من نفس الهاتف.");

            var confirmation = MessageBox.Show(this,
                $"سيتم إرسال Invoke واحد إلى زر Start في Odin عبر Windows UI Automation.\n\nالجهاز: {_deviceManufacturer} {_deviceModel}\nSerial: {_deviceSerial}\nCOM: {report.ComPort}\nالحزم:\n{string.Join("\n", paths.Select(Path.GetFileName))}\n\nتأكد أن معلومات الجهاز وCSC وBinary وAnti-Rollback صحيحة. هذا الإجراء يبدأ التفليش ولا يمكن التراجع عنه بأمان. هل تريد الاستمرار؟",
                "تأكيد نهائي لتفليش Samsung عبر Odin", MessageBoxButton.YesNo, MessageBoxImage.Stop);
            if (confirmation != MessageBoxResult.Yes)
                return;

            _startOdinButton.IsEnabled = false;
            var started = _odinAutomation.InvokeStart(
                _odinExecutable.Text, paths,
                _confirmCompatibility.IsChecked == true,
                _confirmBackup.IsChecked == true,
                _confirmComPort.IsChecked == true,
                report.ProcessId!.Value,
                report.ComPort!);
            _inspection.AppendText(Environment.NewLine + Environment.NewLine +
                "ODIN START RESULT: " + (started.StartInvoked ? "INVOKE SENT" : "BLOCKED") +
                Environment.NewLine + string.Join(Environment.NewLine, started.Decision.Blockers));
            _status.Text = started.StartInvoked
                ? "تم إرسال Invoke إلى Start في Odin. تابع Progress/ID:COM/Result داخل Odin؛ لم يؤكد SoftBox نجاح التفليش."
                : "لم يتم تنفيذ Start: " + string.Join(" | ", started.Decision.Blockers);
        }
        catch (Exception ex)
        {
            _startOdinButton.IsEnabled = false;
            _inspectedOdinProcessId = null;
            _inspectedComPort = null;
            _status.Text = "تم منع أو تعذر بدء تفليش Odin: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Samsung Odin Assistant", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string[] SelectedPackagePaths()
        => _slotPaths.Values
            .Where(text => !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => Path.GetFullPath(text.Text))
            .ToArray();

    private static async Task<string> CalculateSha256Async(string path)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Border Panel()
        => new()
        {
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10)
        };

    private static TextBlock TextBlock(string text, double size, string color)
        => new() { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 120,
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
