using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Fastboot;
using SoftBox.PhoneLab.Core.Firmware;
using System.IO;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Xiaomi/Redmi/POCO Fastboot diagnostics and a restricted single-image write workflow.
/// Bootloader transitions are explicit; unlock, erase, format, relock and arbitrary commands are not exposed.
/// </summary>
public sealed class XiaomiFastbootDiagnosticsWindow : Window
{
    private readonly AdbClient _adb;
    private readonly string _serial;
    private readonly string _manufacturer;
    private readonly string _model;
    private readonly AndroidDeviceToolsService _androidTools;
    private readonly FastbootService _fastboot = new();
    private readonly TextBox _details = new();
    private readonly TextBlock _status = new();
    private readonly Button _enterButton;
    private readonly Button _refreshButton;
    private readonly Button _inspectRomButton;
    private readonly Button _cancelButton;
    private readonly Button _flashImageButton;
    private readonly Button _fullRomButton;
    private readonly Button _officialUnlockButton;
    private readonly ComboBox _flashPartition = new();
    private readonly CheckBox _confirmOfficialSource = new() { Content = "أؤكد أن IMG من مصدر Xiaomi الرسمي ومخصص لـ Product الحالي" };
    private readonly CheckBox _confirmBackup = new() { Content = "أؤكد وجود نسخة احتياطية ووعيي بخطر فقد البيانات أو تعطل الإقلاع" };
    private readonly TextBox _flashConfirmation = new();
    private readonly ComboBox _romProducts = new();
    private readonly HashSet<string> _romFlashPartitions = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastFastbootProduct;
    private FastbootStateReport? _lastFastbootState;
    private string? _lastFastbootSerial;
    private CancellationTokenSource? _operation;

    public XiaomiFastbootDiagnosticsWindow(AdbClient adb, string serial, string manufacturer, string model)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        if (!XiaomiFastbootDiagnosticsPolicy.IsXiaomiManufacturer(manufacturer))
            throw new UnauthorizedAccessException("تم رفض فتح أدوات Xiaomi Fastboot لأن هوية الشركة ليست Xiaomi/Redmi/POCO.");

        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _serial = serial;
        _manufacturer = manufacturer;
        _model = model ?? "";
        _androidTools = new AndroidDeviceToolsService(adb);

        Title = "SoftBox | Xiaomi Fastboot Diagnostics";
        Width = 1000;
        Height = 740;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _enterButton = ButtonControl("إعادة تشغيل الهاتف إلى Fastboot", "#704B20", EnterFastboot_Click);
        _refreshButton = ButtonControl("إعادة فحص Fastboot وقراءة المتغيرات", "#14517A", Refresh_Click);
        _inspectRomButton = ButtonControl("فحص حزمة Fastboot ROM (قراءة فقط)", "#176044", InspectFastbootRom_Click);
        _cancelButton = ButtonControl("إلغاء", "#713B43", Cancel_Click);
        _flashImageButton = ButtonControl("تفليش قسم IMG محدد", "#713B43", FlashImage_Click);
        _fullRomButton = ButtonControl("تفليش ROM كامل متعدد الصور", "#713B43", OpenFullRomFlash_Click);
        _officialUnlockButton = ButtonControl("أداة Xiaomi Mi Unlock الرسمية", "#704B20", (_, _) => new OemOfficialToolsWindow(OemServiceVendor.Xiaomi) { Owner = this }.Show());
        _flashImageButton.IsEnabled = false;
        _refreshButton.IsEnabled = false;
        _cancelButton.IsEnabled = false;
        Content = BuildContent();
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Xiaomi / Redmi / POCO — Fastboot Service and Diagnostics",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = $"الجهاز: {_manufacturer} {_model} | ADB: {Mask(_serial)}",
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 6, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "يقرأ هوية Fastboot ويدعم تفليش IMG واحدة بشروط محددة، أو فتح مسار تفليش ROM كامل متعدد الصور من سكربت مفكوك بعد تحليله وتنفيذه بأوامر منفصلة. لا يتم تشغيل سكربت OEM نفسه. فتح Bootloader عبر Mi Unlock الرسمية فقط.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#F0C9A5"),
            Margin = new Thickness(0, 8, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(_enterButton);
        actions.Children.Add(_refreshButton);
        actions.Children.Add(_inspectRomButton);
        _flashPartition.ItemsSource = XiaomiFastbootFlashPolicy.SupportedPartitions;
        _flashPartition.SelectedIndex = 0;
        _flashPartition.Width = 150;
        _flashPartition.Margin = new Thickness(4);
        _flashPartition.Background = Brush("#0A1B2D");
        _flashPartition.Foreground = Brush("#EAF3FF");
        _romProducts.Width = 160;
        _romProducts.Margin = new Thickness(4);
        _romProducts.Background = Brush("#0A1B2D");
        _romProducts.Foreground = Brush("#EAF3FF");
        _romProducts.BorderBrush = Brush("#35516F");
        _romProducts.ToolTip = "Product من android-info.txt داخل حزمة ROM المفحوصة";
        actions.Children.Add(new TextBlock { Text = "ROM Product", Foreground = Brush("#B7C9DE"), VerticalAlignment = VerticalAlignment.Center });
        actions.Children.Add(_romProducts);
        actions.Children.Add(_flashPartition);
        actions.Children.Add(_flashImageButton);
        actions.Children.Add(_fullRomButton);
        actions.Children.Add(_officialUnlockButton);
        actions.Children.Add(_cancelButton);
        _confirmOfficialSource.Foreground = Brush("#F0C9A5");
        _confirmBackup.Foreground = Brush("#F0C9A5");
        _confirmOfficialSource.Margin = new Thickness(4);
        _confirmBackup.Margin = new Thickness(4);
        actions.Children.Add(_confirmOfficialSource);
        actions.Children.Add(_confirmBackup);
        _flashConfirmation.Width = 220;
        _flashConfirmation.Margin = new Thickness(4);
        _flashConfirmation.ToolTip = "اكتب FLASH ثم مسافة ثم آخر أربعة أحرف من Serial";
        _flashConfirmation.Background = Brush("#071625");
        _flashConfirmation.Foreground = Brush("#DCEBFA");
        _flashConfirmation.BorderBrush = Brush("#263D58");
        _flashConfirmation.Padding = new Thickness(6);
        actions.Children.Add(_flashConfirmation);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.Wrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.Background = Brush("#071625");
        _details.Foreground = Brush("#DCEBFA");
        _details.BorderBrush = Brush("#263D58");
        _details.Padding = new Thickness(10);
        _details.FontFamily = new FontFamily("Consolas");
        _details.FontSize = 10;
        _details.Text = "تأكد من اختيار الهاتف الصحيح. الانتقال إلى Bootloader يوقف جلسة ADB الحالية مؤقتًا.";
        root.Children.Add(_details);

        _status.Text = "لم يتم تنفيذ أي انتقال بعد.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private void OpenFullRomFlash_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
        {
            _status.Text = "انتظر انتهاء عملية Fastboot الحالية قبل فتح أداة ROM كامل.";
            return;
        }

        try
        {
            new XiaomiFastbootRomFlashWindow(_serial, _manufacturer, _model)
            {
                Owner = this
            }.Show();
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح أداة Xiaomi Full ROM: " + ex.Message;
        }
    }

    private async void EnterFastboot_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;
        if (!_fastboot.IsAvailable)
        {
            _status.Text = "Fastboot غير متاح داخل حزمة SoftBox أو PATH؛ لم يتم إعادة تشغيل الهاتف.";
            return;
        }
        var confirmation = MessageBox.Show(this,
            $"سيعيد SoftBox تشغيل هاتف Xiaomi/Redmi/POCO المحدد إلى Bootloader/Fastboot:\n{_model} | {Mask(_serial)}\n\nلن يتم تفليش أو مسح أو فتح Bootloader. قد يتوقف ADB حتى يعاد تشغيل الهاتف. متابعة؟",
            "تأكيد الانتقال إلى Xiaomi Fastboot", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await RunOperationAsync("جارٍ التحقق من ADB وهوية Xiaomi قبل الانتقال...", async token =>
        {
            var state = await _adb.RunForDeviceAsync(_serial, ["get-state"], token, TimeSpan.FromSeconds(12));
            if (!state.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("الهاتف لم يعد في حالة ADB device؛ تم إيقاف العملية.");

            var manufacturer = await _adb.RunForDeviceAsync(
                _serial, ["shell", "getprop", "ro.product.manufacturer"], token, TimeSpan.FromSeconds(12));
            var brand = await _adb.RunForDeviceAsync(
                _serial, ["shell", "getprop", "ro.product.brand"], token, TimeSpan.FromSeconds(12));
            if (!XiaomiFastbootDiagnosticsPolicy.IsXiaomiManufacturer(manufacturer) &&
                !XiaomiFastbootDiagnosticsPolicy.IsXiaomiManufacturer(brand))
                throw new UnauthorizedAccessException("خصائص Android الحالية لا تؤكد Xiaomi/Redmi/POCO؛ لم يتم إرسال أمر reboot.");

            var output = await _androidTools.RebootDeviceAsync(_serial, AndroidRebootTarget.Bootloader, token);
            _status.Text = output + " جارٍ انتظار Fastboot على نفس Serial...";
            var device = await WaitForSelectedFastbootDeviceAsync(token);
            var raw = await _fastboot.ReadAllVariablesAsync(device.Serial, token);
            var parsed = FastbootVariableParser.Parse(raw);
            var report = XiaomiFastbootDiagnosticsPolicy.Evaluate(
                manufacturer + " " + brand, _serial, device.Serial, device.Details, parsed);
            _details.Text = FormatReport(report, raw);
            _lastFastbootProduct = report.Product;
            _lastFastbootState = parsed;
            _lastFastbootSerial = device.Serial;
            _flashImageButton.IsEnabled = parsed.LockState == BootloaderLockState.Unlocked && !string.IsNullOrWhiteSpace(parsed.Product);
            _refreshButton.IsEnabled = true;
            return "ظهر الجهاز في Fastboot وتمت قراءة متغيراته. لم يتم تغيير أي قسم أو إعداد.";
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;
        await RunOperationAsync("جارٍ البحث عن نفس الجهاز في Fastboot وقراءة المتغيرات...", async token =>
        {
            var devices = await _fastboot.DiscoverAsync(token);
            var device = devices.FirstOrDefault(item =>
                item.Serial.Equals(_serial, StringComparison.OrdinalIgnoreCase));
            if (device is null)
            {
                _details.Text = "لم يظهر Serial الخاص بالهاتف المحدد في fastboot devices -l. تم رفض قراءة أي هاتف آخر.";
                return "لم يتم العثور على نفس الهاتف في Fastboot.";
            }
            var raw = await _fastboot.ReadAllVariablesAsync(device.Serial, token);
            var parsed = FastbootVariableParser.Parse(raw);
            var report = XiaomiFastbootDiagnosticsPolicy.Evaluate(
                _manufacturer, _serial, device.Serial, device.Details, parsed);
            _details.Text = FormatReport(report, raw);
            _lastFastbootProduct = report.Product;
            _lastFastbootState = parsed;
            _lastFastbootSerial = device.Serial;
            _flashImageButton.IsEnabled = parsed.LockState == BootloaderLockState.Unlocked && !string.IsNullOrWhiteSpace(parsed.Product);
            return "اكتملت قراءة متغيرات Fastboot للجهاز المحدد.";
        });
    }

    private async void FlashImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "اختيار صورة Xiaomi IMG للقسم المحدد",
            Filter = "Android image (*.img)|*.img",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;
        var imagePath = Path.GetFullPath(dialog.FileName);
        var partition = _flashPartition.SelectedItem as string ?? "";
        var suffix = _serial.Length > 4 ? _serial[^4..] : _serial;
        if (!string.Equals(_flashConfirmation.Text.Trim(), "FLASH " + suffix, StringComparison.Ordinal))
        {
            _status.Text = "اكتب FLASH ثم مسافة وآخر أربعة أحرف من Serial لتأكيد الاستمرار.";
            return;
        }
        if (!_confirmOfficialSource.IsChecked.GetValueOrDefault() || !_confirmBackup.IsChecked.GetValueOrDefault())
        {
            _status.Text = "يلزم تأكيد مصدر OEM والنسخة الاحتياطية قبل أي كتابة.";
            return;
        }

        var packageProduct = _romProducts.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(packageProduct))
        {
            _status.Text = "افحص حزمة Fastboot ROM واختر Product من android-info.txt أولًا.";
            return;
        }
        if (!_romFlashPartitions.Contains(partition))
        {
            _status.Text = "القسم المحدد غير موجود في أوامر flash للحزمة التي تم فحصها؛ تم منع الكتابة.";
            return;
        }
        if (!string.Equals(Path.GetFileName(imagePath), partition + ".img", StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "اسم IMG لا يطابق القسم المحدد. اختر " + partition + ".img.";
            return;
        }

        await RunOperationAsync("جارٍ إعادة فحص هوية Xiaomi وحالة Bootloader قبل التفليش...", async token =>
        {
            var liveDevices = await _fastboot.DiscoverAsync(token);
            var liveDevice = liveDevices.FirstOrDefault(item => item.Serial.Equals(_serial, StringComparison.Ordinal));
            if (liveDevice is null)
                throw new UnauthorizedAccessException("لم يظهر نفس Serial في Fastboot. تم منع الكتابة.");
            var liveRaw = await _fastboot.ReadAllVariablesAsync(_serial, token);
            var liveState = FastbootVariableParser.Parse(liveRaw);
            var decision = XiaomiFastbootFlashPolicy.Evaluate(
                _manufacturer, _serial, liveDevice.Serial, liveState.Product, packageProduct,
                liveState.LockState, partition, imagePath,
                _confirmOfficialSource.IsChecked == true, _confirmBackup.IsChecked == true);
            if (!decision.CanFlash)
                throw new UnauthorizedAccessException("تم منع التفليش:\n" + string.Join("\n", decision.Blockers));
            // Keep a read-only handle open through the fastboot process to prevent replacement on Windows.
            await using var input = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input, token)).ToLowerInvariant();
            var info = new FileInfo(imagePath);
            var confirmation = MessageBox.Show(this,
                $"سيتم كتابة IMG واحدة إلى القسم {partition} على {Mask(_serial)}.\nProduct: {liveState.Product}\nImage: {info.Name}\nSize: {info.Length:N0}\nSHA-256: {hash}\n\nلا توجد عملية Unlock تلقائية. هل تريد الاستمرار؟",
                "تأكيد Xiaomi Fastboot Flash", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
                return "ألغى المستخدم الكتابة.";
            var result = await _fastboot.FlashPartitionAsync(_serial, partition, imagePath, token);
            _details.Text = "XIAOMI FASTBOOT SINGLE-PARTITION FLASH\n" +
                "SERIAL: " + Mask(_serial) + "\nPRODUCT: " + liveState.Product +
                "\nPARTITION: " + partition + "\nIMAGE: " + info.Name +
                "\nSIZE: " + info.Length + "\nSHA-256: " + hash +
                "\n\nFASTBOOT OUTPUT:\n" + result +
                "\n\nلم يتم تنفيذ Unlock أو Erase أو Relock أو Flash لأي قسم آخر.";
            return "انتهى أمر Fastboot للقسم المحدد. أعد قراءة المتغيرات ثم تحقق من إقلاع الجهاز.";
        });
    }

    private async void InspectFastbootRom_Click(object sender, RoutedEventArgs e)    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "اختيار حزمة Xiaomi Fastboot ROM للفحص المحلي",
            Filter = "Xiaomi Fastboot ROM (*.tgz;*.tar.gz;*.tar)|*.tgz;*.tar.gz;*.tar|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        await RunOperationAsync("جارٍ فحص أرشيف Xiaomi Fastboot ROM دون استخراج أو تشغيل سكربتات...", async token =>
        {
            var report = await XiaomiFastbootRomInspector.InspectAsync(dialog.FileName, _lastFastbootProduct, token);
            _romProducts.ItemsSource = report.RequiredProducts;
            _romProducts.SelectedIndex = report.RequiredProducts.Count == 1 ? 0 : -1;
            _romFlashPartitions.Clear();
            foreach (var item in report.FlashPartitions)
                _romFlashPartitions.Add(item);
            _details.Text = FormatRomReport(report);
            return report.ProductMatch switch
            {
                XiaomiRomProductMatchState.Match => "تطابق Product النصي؛ لا يعني ذلك توثيق المصدر أو السماح بالتفليش.",
                XiaomiRomProductMatchState.Mismatch => "Product غير مطابق؛ لا تستخدم الحزمة على الجهاز المحدد.",
                _ => "اكتمل فحص الأرشيف، لكن Product غير معروف أو لم تتم قراءته من Fastboot."
            };
        });
    }

    private static string FormatRomReport(XiaomiFastbootRomInspectionResult report)
    {
        var lines = new List<string>
        {
            "XIAOMI FASTBOOT ROM ARCHIVE INSPECTION — READ ONLY",
            "ARCHIVE: " + report.FileName,
            "SIZE BYTES: " + report.ArchiveBytes,
            "SHA-256: " + report.ArchiveSha256,
            "SELECTED DEVICE PRODUCT: " + (report.SelectedDeviceProduct ?? "Unknown"),
            "PRODUCT MATCH: " + report.ProductMatch,
            "FLASH ALLOWED: " + report.FlashAllowed,
            "UNLOCK ALLOWED: " + report.UnlockAllowed,
            "REQUIRED PRODUCTS: " + (report.RequiredProducts.Count == 0 ? "Unknown" : string.Join(", ", report.RequiredProducts)),
            "IMAGE FILES: " + report.ImageFiles.Count,
            "FLASH SCRIPT FILES: " + (report.ScriptFiles.Count == 0 ? "None found" : string.Join(", ", report.ScriptFiles)),
            "FLASH PARTITIONS REFERENCED IN SCRIPTS: " + (report.FlashPartitions.Count == 0 ? "None found" : string.Join(", ", report.FlashPartitions)),
            "ERASE / WIPE COMMANDS FOUND: " + report.ContainsEraseOrWipeCommands,
            "UNLOCK / LOCK COMMANDS FOUND: " + report.ContainsUnlockCommands,
            "",
            "FINDINGS:"
        };
        lines.AddRange(report.Findings.Select(item => "- " + item));
        lines.Add("");
        lines.Add("IMAGE PATHS (FIRST 100):");
        lines.AddRange(report.ImageFiles.Take(100).Select(item => "- " + item));
        return string.Join(Environment.NewLine, lines);
    }

    private async Task<FastbootDevice> WaitForSelectedFastbootDeviceAsync(CancellationToken cancellationToken)    {
        if (!_fastboot.IsAvailable)
            throw new FileNotFoundException("لم يتم العثور على fastboot.exe في أدوات SoftBox أو PATH.");
        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var devices = await _fastboot.DiscoverAsync(cancellationToken);
            var selected = devices.FirstOrDefault(item =>
                item.Serial.Equals(_serial, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
                return selected;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        throw new TimeoutException("لم يظهر الهاتف المحدد في Fastboot خلال 60 ثانية. تحقق من كابل USB وتعريف Fastboot؛ لم تتم قراءة أي جهاز آخر.");
    }

    private static string FormatReport(XiaomiFastbootDiagnosticsReport report, string raw)
    {
        var lines = new List<string>
        {
            "XIAOMI FASTBOOT INSPECTION — READ ONLY",
            "DEVICE: " + report.DeviceLabel,
            "XIAOMI PROFILE: " + report.IsXiaomiDevice,
            "SELECTED SERIAL MATCH: " + report.SelectedSerialMatches,
            "FLASH ALLOWED: " + report.FlashAllowed,
            "PRODUCT: " + (report.Product ?? "غير متاح"),
            "VARIANT: " + (report.Variant ?? "غير متاح"),
            "HARDWARE REVISION: " + (report.HardwareRevision ?? "غير متاح"),
            "BOOTLOADER VERSION: " + (report.BootloaderVersion ?? "غير متاح"),
            "CURRENT SLOT: " + (report.CurrentSlot ?? "غير متاح"),
            "BOOTLOADER LOCK: " + report.LockState,
            "SECURE BOOT: " + report.SecureBootState,
            "",
            "ANTI-ROLLBACK VARIABLES:"
        };
        lines.AddRange(report.RollbackVariables.Count == 0
            ? ["لا توجد متغيرات معروفة في المخرجات."]
            : report.RollbackVariables.Select(pair => pair.Key + ": " + pair.Value));
        lines.Add("");
        lines.Add("FINDINGS:");
        lines.AddRange(report.Findings.Select(item => "- " + item));
        lines.Add("");
        lines.Add("WARNINGS:");
        lines.AddRange(report.Warnings.Select(item => "- " + item));
        lines.Add("");
        lines.Add("RAW FASTBOOT GETVAR OUTPUT:");
        lines.Add(raw);
        return string.Join(Environment.NewLine, lines);
    }

    private async Task RunOperationAsync(string message, Func<CancellationToken, Task<string>> operation)
    {
        if (_operation is not null)
            return;
        _operation = new CancellationTokenSource();
        _enterButton.IsEnabled = false;
        _refreshButton.IsEnabled = false;
        _inspectRomButton.IsEnabled = false;
        _flashImageButton.IsEnabled = false;
        _officialUnlockButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        _status.Text = message;
        try
        {
            _status.Text = await operation(_operation.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء العملية.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تنفيذ فحص Xiaomi Fastboot: " + ex.Message;
            _details.Text = ex.ToString();
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _enterButton.IsEnabled = true;
            _refreshButton.IsEnabled = true;
            _inspectRomButton.IsEnabled = true;
            _flashImageButton.IsEnabled = _lastFastbootState?.LockState == BootloaderLockState.Unlocked && _lastFastbootSerial == _serial;
            _officialUnlockButton.IsEnabled = true;
            _cancelButton.IsEnabled = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private static string Mask(string serial) => serial.Length > 4 ? "••••" + serial[^4..] : "••••";

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 135,
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
