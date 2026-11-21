using SoftBox.PhoneLab.Core.Diagnostics;
using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Firmware;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
    private bool _packagesInspected;

    public SamsungOdinAssistantWindow()
    {
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
        _launchButton.IsEnabled = false;
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
            "إدارة ملفات BL / AP / CP / CSC وتجهيزها قبل فتح Odin الخارجي. SoftBox لا يوزع Odin ولا يحقن الملفات داخله أو يضغط Start تلقائيًا.",
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
            "اختر CSC أو HOME_CSC فقط، ولا تضع الملفين معًا. AP مطلوب في خطة الحزم. SoftBox يتحقق من الامتداد والملف والبصمة فقط، ولا يقرأ محتويات TAR ولا يثبت توافقها.",
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
            _inspectedHashes.Clear();
            _launchButton.IsEnabled = false;
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
            lines.Add("SoftBox does not automate Odin controls or press Start.");
            _inspection.Text = string.Join(Environment.NewLine, lines);
            _packagesInspected = true;
            _launchButton.IsEnabled = true;
            _status.Text = $"تم فحص {plan.Count} ملف/ملفات وحساب SHA-256 من البايتات الحالية.";
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
