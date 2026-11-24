using SoftBox.PhoneLab.Core.Compatibility;
using System.IO;
using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Firmware;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Core.Recovery;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Offline firmware library for local import, metadata indexing, archive inspection and hash revalidation.
/// It deliberately does not download, flash or claim device compatibility.
/// </summary>
public sealed class FirmwareCenterWindow : Window
{
    private readonly FirmwareLibraryService _library = new();
    private readonly DeviceProfileCatalog _deviceProfiles = new();
    private readonly FirmwareDownloadService _downloadService;
    private readonly ObservableCollection<FirmwareLibraryItem> _items = [];
    private readonly ListBox _list = new();
    private readonly TextBox _manufacturer = new();
    private readonly TextBox _model = new();
    private readonly TextBox _region = new();
    private readonly TextBox _version = new();
    private readonly TextBox _source = new();
    private readonly TextBox _notes = new();
    private readonly TextBox _downloadUrl = new();
    private readonly TextBox _expectedSha256 = new();
    private readonly TextBox _details = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _importButton;
    private readonly Button _verifyButton;
    private readonly Button _inspectButton;
    private readonly Button _preflightButton;
    private readonly Button _downloadButton;
    private readonly Button _cancelButton;
    private readonly DeviceSnapshot? _deviceSnapshot;
    private readonly bool _adbAuthorized;
    private readonly string? _detectedDeviceContext;
    private CancellationTokenSource? _operation;
    private bool _closed;

    public FirmwareCenterWindow(
        DeviceSnapshot? deviceSnapshot = null,
        bool adbAuthorized = false,
        string? detectedManufacturer = null,
        string? detectedMode = null,
        string? detectedHardwareId = null)
    {
        _deviceSnapshot = deviceSnapshot;
        _adbAuthorized = adbAuthorized && deviceSnapshot is not null;
        _detectedDeviceContext = string.IsNullOrWhiteSpace(detectedMode)
            ? null
            : $"Windows USB PnP: {detectedManufacturer ?? "الشركة غير مؤكدة"} • {detectedMode} • {detectedHardwareId ?? "Hardware ID غير متاح"}";
        Title = "SoftBox | مركز Firmware";
        Width = 1120;
        Height = 820;
        MinWidth = 850;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _importButton = ButtonControl("استيراد ملف Firmware محلي", "#14517A", Import_Click);
        _verifyButton = ButtonControl("إعادة التحقق من SHA-256", "#176044", Verify_Click);
        _inspectButton = ButtonControl("فحص الأرشيف", "#243B56", Inspect_Click);
        _preflightButton = ButtonControl("فحص الاستعادة الآمنة", "#704B20", Preflight_Click);
        _downloadButton = ButtonControl("تنزيل / استكمال Firmware", "#14517A", Download_Click);
        _cancelButton = ButtonControl("إلغاء العملية", "#713B43", Cancel_Click);
        _cancelButton.IsEnabled = false;
        _downloadService = new FirmwareDownloadService(_library);

        Content = BuildContent();
        if (!string.IsNullOrWhiteSpace(detectedManufacturer))
            _manufacturer.Text = detectedManufacturer;
        if (_detectedDeviceContext is not null)
        {
            _model.Text = string.Empty;
            _notes.Text = _detectedDeviceContext + Environment.NewLine +
                "الموديل الدقيق لم يُقرأ من وضع الصيانة. أدخله يدويًا من ملصق الجهاز/قاعدة موثوقة قبل تنزيل أو تقييم أي Firmware.";
        }
        _list.SelectionChanged += (_, _) => ShowSelected();
        Loaded += async (_, _) => await LoadItemsAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            if (_operation is not null) _operation.Cancel();
            else _downloadService.Dispose();
        };
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "مركز Firmware",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "FIRMWARE LIBRARY • RESUMABLE HTTPS DOWNLOAD • SHA-256 VERIFICATION",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "يدير مكتبة Firmware المحلية ويتيح تنزيل رابط HTTPS مباشر مع استكمال الأجزاء المتوقفة تلقائيًا، ثم يتحقق من SHA-256 الذي تحصل عليه من المصدر الرسمي قبل إدخال الملف إلى المكتبة. لا يوجد تفليش أو تثبيت تلقائي.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
        });
        if (_detectedDeviceContext is not null)
        {
            header.Children.Add(new TextBlock
            {
                Text = "سياق الجهاز المكتشف: " + _detectedDeviceContext +
                    Environment.NewLine + "تنبيه: ظهور Download Mode لا يكشف وحده رقم الموديل أو المنطقة أو إصدار Bootloader؛ لم نملأ هذه الحقول بالتخمين.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                Background = Brush("#2A211A"),
                Padding = new Thickness(9),
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 10
            });
        }
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var warning = new Border
        {
            Background = Brush("#211D2A"),
            BorderBrush = Brush("#66516B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock
            {
                Text = "استخدم رابط الملف المباشر من بوابة الشركة المصنعة، وأدخل SHA-256 المنشور من المصدر نفسه. لا يتبع SoftBox التحويلات Redirect تلقائيًا، ولا يثبت أن المصدر رسمي لمجرد HTTPS أو تطابق البصمة. التنزيل لا يعني توافق Firmware مع الجهاز، ولا توجد عملية Flash أو Unlock أو Wipe.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(warning, Dock.Top);
        root.Children.Add(warning);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.95, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        var leftTitle = new TextBlock
        {
            Text = "مكتبة Firmware المحلية",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#DCEBFA"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(leftTitle, Dock.Top);
        left.Children.Add(leftTitle);

        _list.ItemsSource = _items;
        _list.DisplayMemberPath = nameof(FirmwareLibraryItem.FileName);
        _list.Background = Brush("#071625");
        _list.Foreground = Brush("#DCEBFA");
        _list.BorderBrush = Brush("#263D58");
        _list.Padding = new Thickness(8);
        left.Children.Add(_list);
        Grid.SetColumn(left, 0);
        Grid.SetRow(left, 0);
        body.Children.Add(left);

        var right = new DockPanel { Margin = new Thickness(8, 0, 0, 0) };
        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(form, "الشركة المصنعة", 0, 0);
        ConfigureInput(_manufacturer);
        Grid.SetRow(_manufacturer, 1);
        Grid.SetColumn(_manufacturer, 0);
        form.Children.Add(_manufacturer);

        AddLabel(form, "الموديل", 0, 1);
        ConfigureInput(_model);
        Grid.SetRow(_model, 1);
        Grid.SetColumn(_model, 1);
        form.Children.Add(_model);

        AddLabel(form, "المنطقة / CSC", 2, 0);
        ConfigureInput(_region);
        Grid.SetRow(_region, 3);
        Grid.SetColumn(_region, 0);
        form.Children.Add(_region);

        AddLabel(form, "الإصدار", 2, 1);
        ConfigureInput(_version);
        Grid.SetRow(_version, 3);
        Grid.SetColumn(_version, 1);
        form.Children.Add(_version);

        AddLabel(form, "وصف المصدر الذي اخترته", 4, 0);
        ConfigureInput(_source);
        Grid.SetRow(_source, 5);
        Grid.SetColumnSpan(_source, 2);
        form.Children.Add(_source);

        AddLabel(form, "ملاحظات فنية", 6, 0);
        _notes.Height = 60;
        _notes.Margin = new Thickness(0, 0, 5, 8);
        _notes.Padding = new Thickness(8);
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.AcceptsReturn = true;
        _notes.Background = Brush("#0A1B2D");
        _notes.Foreground = Brush("#EAF3FF");
        _notes.BorderBrush = Brush("#35516F");
        Grid.SetRow(_notes, 7);
        Grid.SetColumnSpan(_notes, 2);
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.Children.Add(_notes);

        for (var row = 0; row < 4; row++)
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddLabel(form, "رابط HTTPS مباشر من المصدر الرسمي", 8, 0);
        ConfigureInput(_downloadUrl);
        _downloadUrl.ToolTip = "الرابط النهائي للملف مع الامتداد المدعوم؛ يجب ألا يحتاج إلى تحويل Redirect.";
        Grid.SetRow(_downloadUrl, 9);
        Grid.SetColumnSpan(_downloadUrl, 2);
        form.Children.Add(_downloadUrl);

        AddLabel(form, "SHA-256 المنشور من المصدر (64 رقمًا سداسيًا)", 10, 0);
        ConfigureInput(_expectedSha256);
        _expectedSha256.ToolTip = "البصمة الأصلية التي تحصل عليها من صفحة الشركة المصنعة أو قناة موثوقة مستقلة.";
        Grid.SetRow(_expectedSha256, 11);
        Grid.SetColumnSpan(_expectedSha256, 2);
        form.Children.Add(_expectedSha256);

        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        buttons.Children.Add(_importButton);
        buttons.Children.Add(_verifyButton);
        buttons.Children.Add(_inspectButton);
        buttons.Children.Add(_preflightButton);
        buttons.Children.Add(_downloadButton);
        buttons.Children.Add(_cancelButton);
        DockPanel.SetDock(buttons, Dock.Top);
        right.Children.Add(buttons);

        DockPanel.SetDock(form, Dock.Top);
        right.Children.Add(form);

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
        _details.Text = "اختر ملفًا من المكتبة لعرض بياناته وبصمته.";
        right.Children.Add(_details);
        Grid.SetColumn(right, 1);
        Grid.SetRow(right, 0);
        body.Children.Add(right);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Text = "جارٍ تحميل مكتبة Firmware المحلية...";
        _status.Foreground = Brush("#9FC7E8");
        _status.FontSize = 11;
        footer.Children.Add(_status);
        _progress.Height = 4;
        _progress.IsIndeterminate = true;
        _progress.Visibility = Visibility.Collapsed;
        _progress.Foreground = Brush("#38BDF8");
        footer.Children.Add(_progress);
        Grid.SetColumnSpan(footer, 2);
        Grid.SetRow(footer, 1);
        body.Children.Add(footer);

        root.Children.Add(body);
        return root;
    }

    private async Task LoadItemsAsync()
    {
        try
        {
            var items = await _library.GetItemsAsync();
            _items.Clear();
            foreach (var item in items)
                _items.Add(item);
            _status.Text = $"تم تحميل {_items.Count:N0} ملف من مكتبة Firmware المحلية.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر قراءة المكتبة المحلية: " + ex.Message;
        }
    }


    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(_downloadUrl.Text.Trim(), UriKind.Absolute, out var sourceUri))
        {
            _status.Text = "أدخل رابط HTTPS مباشرًا لملف Firmware.";
            return;
        }

        var progress = new Progress<FirmwareDownloadProgress>(update =>
        {
            _status.Text = update.Status;
            if (update.TotalBytes is > 0)
            {
                _progress.IsIndeterminate = false;
                _progress.Maximum = update.TotalBytes.Value;
                _progress.Value = Math.Min(update.BytesReceived, update.TotalBytes.Value);
            }
            else
            {
                _progress.IsIndeterminate = true;
            }
        });

        await RunOperationAsync("جارٍ تجهيز تنزيل Firmware والتحقق من المصدر...", async token =>
        {
            var result = await _downloadService.DownloadAndImportAsync(
                sourceUri,
                _expectedSha256.Text,
                _manufacturer.Text,
                _model.Text,
                _region.Text,
                _version.Text,
                _notes.Text,
                _source.Text,
                progress,
                token);

            await LoadItemsAsync();
            _list.SelectedItem = _items.FirstOrDefault(candidate => candidate.Id == result.LibraryItem.Id);
            ShowSelected();
            _details.Text += Environment.NewLine + Environment.NewLine +
                "DOWNLOAD RESULT: Imported into the local Firmware library." + Environment.NewLine +
                "VERIFIED SHA-256: " + result.VerifiedSha256 + Environment.NewLine +
                "BYTES: " + result.DownloadedBytes.ToString("N0") + Environment.NewLine +
                "RESUMED: " + result.Resumed + Environment.NewLine +
                "PUBLISHER AUTHENTICITY: NOT ESTABLISHED" + Environment.NewLine +
                "DEVICE COMPATIBILITY: NOT ESTABLISHED";
            _progress.IsIndeterminate = false;
            _progress.Maximum = 100;
            _progress.Value = 100;
            return $"اكتمل {(result.Resumed ? "استكمال " : "")}تنزيل Firmware والتحقق من SHA-256، وأُضيف الملف إلى المكتبة: {result.LibraryItem.FileName}";
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => _operation?.Cancel();

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار ملف Firmware من الكمبيوتر",
            Filter = "Supported firmware|*.zip;*.img;*.bin;*.tar;*.md5;*.ofp;*.ozip;*.payload|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        await RunOperationAsync("جارٍ نسخ الملف إلى مكتبة Firmware وحساب SHA-256...", async token =>
        {
            var item = await _library.ImportAsync(
                dialog.FileName,
                _manufacturer.Text,
                _model.Text,
                _region.Text,
                _version.Text,
                _source.Text,
                _notes.Text,
                token);
            await LoadItemsAsync();
            _list.SelectedItem = _items.FirstOrDefault(candidate => candidate.Id == item.Id);
            ShowSelected();
            return "تم استيراد الملف وتسجيل بصمته في المكتبة المحلية. لم يتم التحقق من مصدره أو توافقه.";
        });
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        if (_list.SelectedItem is not FirmwareLibraryItem item)
            return;

        await RunOperationAsync("جارٍ إعادة حساب SHA-256 ومقارنته بسجل الاستيراد...", async token =>
        {
            var result = await _library.VerifyAsync(item.Id, token);
            _details.Text = FormatIntegrity(result);
            return result.FileExists && result.SizeMatches && result.Sha256Matches
                ? "تطابقت بصمة الملف مع سجل الاستيراد المحلي. لم يتم إثبات المصدر أو التوافق."
                : "التحقق فشل أو الملف مفقود/متغير؛ لا تستخدم الملف قبل مراجعة المشكلة.";
        });
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e)
    {
        if (_list.SelectedItem is not FirmwareLibraryItem item)
            return;

        await RunOperationAsync("جارٍ فحص بنية ملف Firmware محليًا...", async token =>
        {
            var result = await AndroidForensicsService.InspectFirmwareArchiveAsync(
                _library.GetAbsolutePath(item), token);
            var text = new StringBuilder();
            text.AppendLine("FILE: " + result.FileName);
            text.AppendLine("SIZE: " + result.SizeBytes);
            text.AppendLine("SHA-256: " + result.Sha256);
            text.AppendLine("ZIP: " + result.IsZip);
            text.AppendLine("ANDROID METADATA: " + result.HasAndroidMetadata);
            text.AppendLine("PAYLOAD: " + result.HasPayload);
            text.AppendLine("UPDATER SCRIPT: " + result.HasUpdaterScript);
            text.AppendLine();
            foreach (var metadata in result.MetadataLines)
                text.AppendLine(metadata);
            text.AppendLine();
            text.AppendLine(result.Assessment);
            text.AppendLine();
            text.AppendLine("This is local archive inspection only. No publisher trust or device compatibility is inferred.");
            _details.Text = text.ToString();
            return "اكتمل فحص البنية المحلية. راجع حدود الفحص قبل استخدام الملف.";
        });
    }

    private async void Preflight_Click(object sender, RoutedEventArgs e)
    {
        if (_list.SelectedItem is not FirmwareLibraryItem item)
        {
            _status.Text = "اختر ملف Firmware من المكتبة أولًا.";
            return;
        }

        await RunOperationAsync("جارٍ إعادة التحقق من الملف وفحص شروط الاستعادة الآمنة...", async token =>
        {
            // Re-hash the file immediately before evaluating the preflight report.
            var integrity = await _library.VerifyAsync(item.Id, token);
            var profileCatalog = await _deviceProfiles.LoadAsync();
            var report = SafeRecoveryPreflightService.Evaluate(
                _deviceSnapshot,
                _adbAuthorized && _deviceSnapshot is not null,
                item,
                integrity,
                profileCatalog.Profiles);

            var compatibility = FirmwareCompatibilityPolicy.Evaluate(
                _deviceSnapshot?.Manufacturer,
                _deviceSnapshot?.ModelNumber,
                _deviceSnapshot?.Product,
                _deviceSnapshot?.Board,
                null,
                null,
                item.Manufacturer,
                item.Model,
                null,
                null,
                item.Region,
                null,
                integrity.FileExists && integrity.SizeMatches && integrity.Sha256Matches,
                false);

            var lines = new List<string>
            {
                "SOFTBOX SAFE RECOVERY — PREFLIGHT ONLY",
                "DEVICE: " + report.DeviceLabel,
                "FIRMWARE: " + report.FirmwareLabel,
                "EVALUATED UTC: " + report.EvaluatedAtUtc.ToString("O"),
                "CAN PROCEED: " + report.CanProceed,
                "",
                "CHECKS:"
            };

            foreach (var check in report.Checks)
            {
                var state = check.State switch
                {
                    RecoveryCheckState.Passed => "PASS",
                    RecoveryCheckState.Failed => "FAIL",
                    RecoveryCheckState.Unknown => "UNKNOWN",
                    RecoveryCheckState.Blocked => "BLOCKED",
                    _ => "UNKNOWN"
                };
                lines.Add($"[{state}] {check.Title}");
                lines.Add("Evidence: " + check.Evidence);
                lines.Add("Required action: " + check.RequiredAction);
                lines.Add("");
            }

            lines.Add("");
            lines.Add("EXACT FIRMWARE COMPATIBILITY GATE");
            lines.Add("CAN PROCEED: " + compatibility.CanProceed);
            foreach (var check in compatibility.Checks)
            {
                var state = check.State switch
                {
                    FirmwareCompatibilityState.Passed => "PASS",
                    FirmwareCompatibilityState.Failed => "FAIL",
                    _ => "UNKNOWN"
                };
                lines.Add($"[{state}] {check.Name}: {check.Evidence}");
                lines.Add("Required action: " + check.RequiredAction);
            }

            lines.Add("");
            lines.Add("LIMITATIONS:");
            lines.AddRange(report.Limitations.Select(limit => "- " + limit));
            lines.AddRange(compatibility.Limitations.Select(limit => "- " + limit));
            lines.Add("");
            lines.Add("No firmware was flashed, no bootloader state was changed and no device data was modified.");
            _details.Text = string.Join(Environment.NewLine, lines);

            var blocked = report.Checks.Count(check =>
                check.State is RecoveryCheckState.Blocked or RecoveryCheckState.Failed or RecoveryCheckState.Unknown);
            var compatibilityBlocked = compatibility.Checks.Count(check =>
                check.State is FirmwareCompatibilityState.Failed or FirmwareCompatibilityState.Unknown);
            return report.CanProceed && compatibility.CanProceed
                ? "انتهى الفحص المسبق والتوافق؛ لا توجد عملية Flash متاحة من هذه الشاشة."
                : $"الاستعادة ممنوعة حاليًا. شروط الفحص المسبق غير المجتازة: {blocked}، وشروط توافق Firmware غير المجتازة: {compatibilityBlocked}. لم يتم تنفيذ أي تغيير على الهاتف.";
        });
    }

    private void ShowSelected()
    {
        if (_list.SelectedItem is not FirmwareLibraryItem item)
            return;

        var text = new StringBuilder();
        text.AppendLine("MANUFACTURER: " + item.Manufacturer);
        text.AppendLine("MODEL: " + item.Model);
        text.AppendLine("REGION: " + (string.IsNullOrWhiteSpace(item.Region) ? "Unspecified" : item.Region));
        text.AppendLine("VERSION: " + (string.IsNullOrWhiteSpace(item.Version) ? "Unspecified" : item.Version));
        text.AppendLine("FILE: " + item.FileName);
        text.AppendLine("SIZE: " + item.SizeBytes);
        text.AppendLine("SHA-256: " + item.Sha256);
        text.AppendLine("IMPORTED UTC: " + item.ImportedAtUtc.ToString("O"));
        text.AppendLine("SOURCE NOTE: " + item.SourceDescription);
        text.AppendLine("NOTES: " + item.Notes);
        text.AppendLine("PATH: " + _library.GetAbsolutePath(item));
        text.AppendLine();
        text.AppendLine("Hash equality proves only that the local file matches its import record. It does not prove the firmware publisher, signature, model, region or rollback compatibility.");
        _details.Text = text.ToString();
    }

    private static string FormatIntegrity(FirmwareIntegrityResult result)
        => string.Join(Environment.NewLine,
        [
            "FIRMWARE FILE: " + result.FileName,
            "EXISTS: " + result.FileExists,
            "SIZE MATCH: " + result.SizeMatches,
            "SHA-256 MATCH: " + result.Sha256Matches,
            "ACTUAL SIZE: " + (result.ActualSizeBytes?.ToString() ?? "Unavailable"),
            "ACTUAL SHA-256: " + (result.ActualSha256 ?? "Unavailable"),
            "",
            result.Message
        ]);

    private async Task RunOperationAsync(string message, Func<CancellationToken, Task<string>> operation)
    {
        if (_operation is not null)
            return;

        _operation = new CancellationTokenSource();
        SetBusy(true, message);
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
            _status.Text = "فشلت العملية: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Firmware Center",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false, _status.Text);
            if (_closed) _downloadService.Dispose();
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _importButton.IsEnabled = !busy;
        _verifyButton.IsEnabled = !busy;
        _inspectButton.IsEnabled = !busy;
        _preflightButton.IsEnabled = !busy;
        _downloadButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
        if (!busy)
        {
            _progress.IsIndeterminate = true;
            _progress.Value = 0;
        }
    }

    private static void AddLabel(Grid grid, string text, int row, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = Brush("#9FC7E8"),
            Margin = new Thickness(0, 0, 4, 5)
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private static void ConfigureInput(TextBox textBox)
    {
        textBox.Height = 34;
        textBox.Margin = new Thickness(0, 0, 5, 8);
        textBox.Padding = new Thickness(8, 6, 8, 6);
        textBox.Background = Brush("#0A1B2D");
        textBox.Foreground = Brush("#EAF3FF");
        textBox.BorderBrush = Brush("#35516F");
    }

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 112,
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
