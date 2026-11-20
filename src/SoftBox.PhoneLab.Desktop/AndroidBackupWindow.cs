using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Backup;
using System.IO;
using SoftBox.PhoneLab.Desktop.Services;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Creates and verifies backups of user-accessible Android shared-storage directories.
/// App-private data and restore-to-phone remain out of scope. SBX encryption is optional.
/// </summary>
public sealed class AndroidBackupWindow : Window
{
    private readonly AndroidBackupService _service;
    private readonly string _serial;
    private readonly TextBox _remotePath = new();
    private readonly TextBox _archivePath = new();
    private readonly TextBox _restoreRoot = new();
    private readonly TextBox _output = new();
    private readonly CheckBox _encryptBackup = new();
    private readonly PasswordBox _password = new();
    private readonly TextBlock _passwordLabel = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _backupButton;
    private readonly Button _verifyButton;
    private readonly Button _restoreButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _operation;

    public AndroidBackupWindow(AdbClient adb, string serial)
    {
        if (!AdbClient.IsSafeSerial(serial))
            throw new ArgumentException("معرف ADB غير صالح.", nameof(serial));
        _serial = serial;
        _service = new AndroidBackupService(adb);

        Title = $"SoftBox | النسخ الاحتياطي الذكي — {_serial}";
        Width = 900;
        Height = 690;
        MinWidth = 720;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _backupButton = ButtonControl("إنشاء نسخة احتياطية", "#14517A", Backup_Click);
        _verifyButton = ButtonControl("التحقق من نسخة موجودة", "#176044", Verify_Click);
        _restoreButton = ButtonControl("استعادة الملفات إلى الهاتف", "#6B4C21", Restore_Click);
        _cancelButton = ButtonControl("إلغاء", "#51333A", Cancel_Click);
        _cancelButton.IsEnabled = false;
        _encryptBackup.Content = "تشفير النسخة بكلمة مرور قوية داخل ملف SBX";
        _encryptBackup.Foreground = Brush("#DCEBFA");
        _encryptBackup.Margin = new Thickness(0, 4, 0, 8);
        _encryptBackup.Checked += (_, _) => UpdateEncryptionVisibility();
        _encryptBackup.Unchecked += (_, _) => UpdateEncryptionVisibility();
        Content = BuildContent();
        UpdateEncryptionVisibility();
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "النسخ الاحتياطي الذكي",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "ANDROID SHARED STORAGE • ZIP ARCHIVE • PER-FILE SHA-256",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "الجهاز: " + _serial + ". اختر مجلدًا عامًا يمكن للهاتف إتاحته عبر ADB مثل DCIM أو Pictures أو Documents. تتم قراءة الملفات ثم إنشاء ZIP يتضمن Manifest وبصمة SHA-256 لكل ملف.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 9, 0, 0),
            FontSize = 12
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var warning = new Border
        {
            Background = Brush("#211D2A"),
            BorderBrush = Brush("#66516B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 14),
            Child = new TextBlock
            {
                Text = "تقتصر النسخة على التخزين المشترك الذي يسمح به Android. يمكنك إنشاء ZIP عادي أو ملف SBX مشفر بكلمة مرور؛ تتيح الاستعادة نسخ الملفات التي تحقق منها SoftBox إلى مجلد جديد على الهاتف دون حذف أو استبدال ملفات قديمة؛ لا يتم نسخ بيانات التطبيقات الخاصة.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(warning, Dock.Top);
        root.Children.Add(warning);

        var form = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(form, "مجلد Android المطلوب نسخه", 0);
        _remotePath.Text = "/sdcard/DCIM";
        ConfigureInput(_remotePath);
        Grid.SetRow(_remotePath, 1);
        Grid.SetColumn(_remotePath, 0);
        Grid.SetColumnSpan(_remotePath, 2);
        form.Children.Add(_remotePath);

        AddLabel(form, "مسار النسخة الاحتياطية على الكمبيوتر", 2);
        _archivePath.Height = 36;
        _archivePath.Margin = new Thickness(0, 0, 0, 8);
        _archivePath.Padding = new Thickness(9, 6, 9, 6);
        _archivePath.Background = Brush("#0A1B2D");
        _archivePath.Foreground = Brush("#EAF3FF");
        _archivePath.BorderBrush = Brush("#35516F");
        Grid.SetRow(_archivePath, 3);
        Grid.SetColumn(_archivePath, 0);
        form.Children.Add(_archivePath);

        var browse = ButtonControl("اختيار مكان الحفظ", "#243B56", BrowseArchive_Click);
        Grid.SetRow(browse, 3);
        Grid.SetColumn(browse, 1);
        form.Children.Add(browse);

        Grid.SetRow(_encryptBackup, 4);
        Grid.SetColumnSpan(_encryptBackup, 2);
        form.Children.Add(_encryptBackup);

        _passwordLabel.Text = "كلمة مرور التشفير (12 حرفًا على الأقل)";
        _passwordLabel.Foreground = Brush("#9FC7E8");
        _passwordLabel.Margin = new Thickness(0, 0, 0, 5);
        Grid.SetRow(_passwordLabel, 5);
        Grid.SetColumnSpan(_passwordLabel, 2);
        form.Children.Add(_passwordLabel);

        _password.Height = 36;
        _password.Margin = new Thickness(0, 0, 0, 8);
        _password.Padding = new Thickness(9, 6, 9, 6);
        _password.Background = Brush("#0A1B2D");
        _password.Foreground = Brush("#EAF3FF");
        _password.BorderBrush = Brush("#35516F");
        _password.ToolTip = "لا يتم حفظ كلمة المرور في ملف الإعدادات أو سجل SoftBox.";
        Grid.SetRow(_password, 6);
        Grid.SetColumnSpan(_password, 2);
        form.Children.Add(_password);

        AddLabel(form, "المجلد الأب لاستعادة الملفات (سينشأ مجلد جديد بداخله)", 7);
        _restoreRoot.Text = "/sdcard/SoftBox-Restores";
        ConfigureInput(_restoreRoot);
        Grid.SetRow(_restoreRoot, 8);
        Grid.SetColumnSpan(_restoreRoot, 2);
        form.Children.Add(_restoreRoot);

        DockPanel.SetDock(form, Dock.Top);
        root.Children.Add(form);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(_backupButton);
        actions.Children.Add(_verifyButton);
        actions.Children.Add(_restoreButton);
        actions.Children.Add(_cancelButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Text = "اختر مصدر النسخ ومسار الحفظ.";
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

        _output.IsReadOnly = true;
        _output.AcceptsReturn = true;
        _output.TextWrapping = TextWrapping.Wrap;
        _output.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _output.Background = Brush("#071625");
        _output.Foreground = Brush("#DCEBFA");
        _output.BorderBrush = Brush("#263D58");
        _output.Padding = new Thickness(12);
        _output.FontFamily = new FontFamily("Consolas");
        _output.FontSize = 10;
        _output.Text = "نتيجة إنشاء النسخة أو التحقق منها ستظهر هنا.";
        root.Children.Add(_output);
        return root;
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var encrypted = _encryptBackup.IsChecked == true;
        if (encrypted)
        {
            try { EncryptedBackupService.ValidatePassword(_password.Password); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "كلمة مرور النسخ المشفر",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(_archivePath.Text))
        {
            BrowseArchive_Click(sender, e);
            if (string.IsNullOrWhiteSpace(_archivePath.Text))
                return;
        }

        await RunOperationAsync(
            encrypted ? "جارٍ نسخ مجلد Android وتشفير الأرشيف..." : "جارٍ نسخ مجلد Android وإنشاء بصمات SHA-256...",
            async token =>
            {
                var progress = new Progress<string>(message => _status.Text = message);
                var manifest = encrypted
                    ? await _service.BackupSharedDirectoryEncryptedAsync(
                        _serial, _remotePath.Text, _archivePath.Text, _password.Password, progress, token)
                    : await _service.BackupSharedDirectoryAsync(
                        _serial, _remotePath.Text, _archivePath.Text, progress, token);

                var summary = new StringBuilder();
                summary.AppendLine("BACKUP: SUCCESS");
                summary.AppendLine("SOURCE DEVICE: " + manifest.SourceDevice);
                summary.AppendLine("SOURCE PATH: " + manifest.SourcePath);
                summary.AppendLine("CREATED UTC: " + manifest.CreatedAtUtc.ToString("O"));
                summary.AppendLine("FILES: " + manifest.FileCount);
                summary.AppendLine("TOTAL BYTES: " + manifest.TotalBytes);
                summary.AppendLine("ARCHIVE: " + _archivePath.Text);
                summary.AppendLine("ENCRYPTION: " + (encrypted
                    ? "AES-256-CBC + HMAC-SHA256; PBKDF2-HMAC-SHA256 (600,000 iterations). Password is not saved."
                    : "Not encrypted. Store this ZIP in a private location."));
                summary.AppendLine();
                summary.AppendLine(manifest.SecurityNotice);
                summary.AppendLine("SHA-256 manifest written for every backed-up file.");
                _output.Text = summary.ToString();
                return encrypted
                    ? $"اكتملت النسخة المشفرة: {manifest.FileCount:N0} ملف، {manifest.TotalBytes:N0} بايت."
                    : $"اكتملت النسخة غير المشفرة: {manifest.FileCount:N0} ملف، {manifest.TotalBytes:N0} بايت.";
            });
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار نسخة SoftBox للتحقق منها",
            Filter = "SoftBox Backup (*.zip;*.sbx)|*.zip;*.sbx|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var encrypted = Path.GetExtension(dialog.FileName).Equals(".sbx", StringComparison.OrdinalIgnoreCase);
        if (encrypted && _encryptBackup.IsChecked != true)
            _encryptBackup.IsChecked = true;
        if (encrypted)
        {
            try { EncryptedBackupService.ValidatePassword(_password.Password); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "أدخل كلمة مرور SBX في الحقل المخصص أولًا. " + ex.Message,
                    "التحقق من النسخة المشفرة", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        await RunOperationAsync("جارٍ مصادقة النسخة وفحص Manifest وبصمات الملفات...", async token =>
        {
            var result = encrypted
                ? await _service.VerifyEncryptedBackupAsync(dialog.FileName, _password.Password, token)
                : await _service.VerifyArchiveAsync(dialog.FileName, token);
            var summary = new StringBuilder();
            summary.AppendLine("BACKUP VERIFICATION: " + (result.IsValid ? "VALID" : "FAILED"));
            summary.AppendLine("CONTAINER: " + (encrypted ? "Encrypted SBX" : "ZIP"));
            summary.AppendLine("VERIFIED FILES: " + result.VerifiedFiles);
            summary.AppendLine("VERIFIED BYTES: " + result.VerifiedBytes);
            summary.AppendLine();
            foreach (var error in result.Errors)
                summary.AppendLine("ERROR: " + error);
            foreach (var warning in result.Warnings)
                summary.AppendLine("WARNING: " + warning);
            _output.Text = summary.ToString();
            if (!result.IsValid)
                throw new InvalidDataException($"النسخة غير صالحة: {result.Errors.Count} مشكلة.");
            return encrypted
                ? $"نجحت مصادقة SBX والتحقق من {result.VerifiedFiles:N0} ملف."
                : $"نجح التحقق من {result.VerifiedFiles:N0} ملف بمطابقة SHA-256.";
        });
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار نسخة SoftBox لاستعادة الملفات إلى الهاتف",
            Filter = "SoftBox Backup (*.zip;*.sbx)|*.zip;*.sbx|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var encrypted = Path.GetExtension(dialog.FileName).Equals(".sbx", StringComparison.OrdinalIgnoreCase);
        if (encrypted && _encryptBackup.IsChecked != true)
            _encryptBackup.IsChecked = true;
        if (encrypted)
        {
            try { EncryptedBackupService.ValidatePassword(_password.Password); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "أدخل كلمة مرور SBX في الحقل المخصص أولًا. " + ex.Message,
                    "استعادة النسخة المشفرة", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var parent = _restoreRoot.Text.Trim();
        try { _ = AndroidDeviceToolsService.ValidatePublicStoragePath(parent); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "مسار الاستعادة غير صالح",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"سيتم أولًا التحقق من سلامة النسخة{(encrypted ? " ومصادقة SBX بكلمة المرور" : "")}، ثم نسخ الملفات إلى مجلد جديد وفريد داخل:\n{parent}\n\nلن يحذف SoftBox أي ملف موجود ولن يكتب فوق مجلد المصدر. قد تترك عملية انقطاع الاتصال بعض الملفات في مجلد الاستعادة الجديد. هل تتابع؟",
            "تأكيد استعادة الملفات", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
            return;

        await RunOperationAsync("جارٍ التحقق من النسخة ثم استعادة الملفات إلى مجلد جديد على الهاتف...", async token =>
        {
            var progress = new Progress<string>(message => _status.Text = message);
            var result = await _service.RestoreToNewAndroidDirectoryAsync(
                _serial,
                dialog.FileName,
                parent,
                encrypted ? _password.Password : null,
                progress,
                token);

            var summary = new StringBuilder();
            summary.AppendLine("BACKUP RESTORE: " + (result.Errors.Count == 0 ? "SUCCESS" : "PARTIAL"));
            summary.AppendLine("DEVICE: " + (_serial.Length > 4 ? "••••" + _serial[^4..] : "••••"));
            summary.AppendLine("CONTAINER: " + (result.WasEncrypted ? "Authenticated SBX" : "ZIP with SHA-256 manifest"));
            summary.AppendLine("REMOTE DIRECTORY: " + result.RemoteRestoreDirectory);
            summary.AppendLine("RESTORED FILES: " + result.RestoredFiles + " / " + result.TotalFiles);
            summary.AppendLine("RESTORED BYTES: " + result.RestoredBytes);
            summary.AppendLine("EXISTING FILES: NOT OVERWRITTEN");
            foreach (var error in result.Errors)
                summary.AppendLine("ERROR: " + error);
            _output.Text = summary.ToString();

            return result.Errors.Count == 0
                ? $"اكتملت الاستعادة داخل {result.RemoteRestoreDirectory}."
                : $"اكتملت الاستعادة جزئيًا: {result.RestoredFiles}/{result.TotalFiles}. راجع الأخطاء؛ لم تُحذف الملفات.";
        });
    }

    private void BrowseArchive_Click(object sender, RoutedEventArgs e)
    {
        var encrypted = _encryptBackup.IsChecked == true;
        var extension = encrypted ? ".sbx" : ".zip";
        var dialog = new SaveFileDialog
        {
            Title = "حفظ النسخة الاحتياطية",
            Filter = encrypted ? "Encrypted SoftBox Backup (*.sbx)|*.sbx" : "SoftBox Backup (*.zip)|*.zip",
            FileName = $"SoftBox-Android-Backup-{DateTime.Now:yyyyMMdd-HHmmss}{extension}",
            AddExtension = true,
            DefaultExt = extension
        };
        if (dialog.ShowDialog(this) == true)
            _archivePath.Text = dialog.FileName;
    }

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
            MessageBox.Show(this, ex.Message, "Smart Backup",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false, _status.Text);
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _backupButton.IsEnabled = !busy;
        _verifyButton.IsEnabled = !busy;
        _restoreButton.IsEnabled = !busy;
        _encryptBackup.IsEnabled = !busy;
        _password.IsEnabled = !busy;
        _restoreRoot.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
    }

    private void UpdateEncryptionVisibility()
    {
        var visible = _encryptBackup.IsChecked == true;
        _passwordLabel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _password.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var extension = visible ? ".sbx" : ".zip";
        if (!string.IsNullOrWhiteSpace(_archivePath.Text) &&
            (Path.GetExtension(_archivePath.Text).Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(_archivePath.Text).Equals(".sbx", StringComparison.OrdinalIgnoreCase)))
            _archivePath.Text = Path.ChangeExtension(_archivePath.Text, extension);
    }

    private static void AddLabel(Grid grid, string text, int row)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = Brush("#9FC7E8"),
            Margin = new Thickness(0, 0, 0, 5)
        };
        Grid.SetRow(label, row);
        Grid.SetColumnSpan(label, 2);
        grid.Children.Add(label);
    }

    private static void ConfigureInput(TextBox textBox)
    {
        textBox.Height = 36;
        textBox.Margin = new Thickness(0, 0, 0, 8);
        textBox.Padding = new Thickness(9, 6, 9, 6);
        textBox.Background = Brush("#0A1B2D");
        textBox.Foreground = Brush("#EAF3FF");
        textBox.BorderBrush = Brush("#35516F");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 100,
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
