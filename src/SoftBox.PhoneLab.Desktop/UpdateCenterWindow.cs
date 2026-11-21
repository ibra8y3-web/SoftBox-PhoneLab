using SoftBox.PhoneLab.Core.Updates;
using SoftBox.PhoneLab.Core.Plugins;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Checks public SoftBox GitHub Releases and opens the official release page on user request.
/// It never downloads or installs update binaries.
/// </summary>
public sealed class UpdateCenterWindow : Window
{
    private readonly ReleaseUpdateService _service = new();
    private readonly PluginPublisherTrustStore _trustStore = new();
    private readonly SignedUpdatePackageService _packageService;
    private readonly ComboBox _channel = new();
    private readonly ComboBox _assets = new();
    private readonly Button _checkButton;
    private readonly Button _openButton;
    private readonly Button _trustKeyButton;
    private readonly Button _stageButton;
    private readonly Button _installButton;
    private readonly Button _rollbackButton;
    private readonly TextBox _details = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private ReleaseUpdateResult? _lastResult;
    private SignedUpdateStageInfo? _stagedUpdate;
    private SignedUpdateRollbackState? _rollbackState;
    private CancellationTokenSource? _operation;

    private string CurrentVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            return version is null ? "0.0.0" : $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}";
        }
    }

    public UpdateCenterWindow()
    {
        Title = "SoftBox | مركز التحديثات";
        Width = 980;
        Height = 740;
        MinWidth = 780;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _packageService = new SignedUpdatePackageService(trustStore: _trustStore);
        _checkButton = ButtonControl("فحص الإصدارات", "#14517A", Check_Click);
        _openButton = ButtonControl("فتح صفحة الإصدار الرسمية", "#176044", OpenRelease_Click);
        _trustKeyButton = ButtonControl("استيراد مفتاح توقيع SoftBox", "#704B20", TrustUpdatePublisherKey_Click);
        _stageButton = ButtonControl("تحقق وتنزيل وتجهيز التحديث", "#176044", StageUpdate_Click);
        _installButton = ButtonControl("تثبيت التحديث بعد الإغلاق", "#14517A", InstallUpdate_Click);
        _rollbackButton = ButtonControl("الرجوع للنسخة السابقة", "#713B43", Rollback_Click);
        _openButton.IsEnabled = false;
        _stageButton.IsEnabled = false;
        _installButton.IsEnabled = false;
        _rollbackButton.IsEnabled = false;
        Content = BuildContent();
        Loaded += async (_, _) =>
        {
            try
            {
                _rollbackState = await _packageService.ReadRollbackStateAsync();
                _rollbackButton.IsEnabled = _rollbackState is not null;
                if (_rollbackState is not null)
                    _status.Text = $"تتوفر نسخة رجوع: {_rollbackState.PreviousVersion} (المثبت حاليًا {_rollbackState.InstalledVersion}).";
            }
            catch (Exception ex) { _status.Text = "تعذر قراءة بيانات الرجوع: " + ex.Message; }
        };
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "مركز تحديثات SoftBox",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "OFFICIAL GITHUB RELEASE CHECKER • STABLE / BETA • NO AUTOMATIC INSTALL",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = $"الإصدار الحالي المقروء من التطبيق: {CurrentVersion}",
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
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
                Text = "لا يقبل SoftBox إلا حزمة Windows التي تحتوي Manifest موقّعًا بمفتاح RSA موثوق، وتطابق بصمة كل ملف. التثبيت يستلزم إغلاق البرنامج، وينشئ نسخة رجوع قبل استبدال الملفات. مفاتيح الناشرين تُستورد يدويًا من قناة موثوقة مستقلة.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(warning, Dock.Top);
        root.Children.Add(warning);

        var filters = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "قناة التحديث",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("#9FC7E8"),
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(label, 0);
        filters.Children.Add(label);

        _channel.ItemsSource = new[]
        {
            new ChannelOption(UpdateChannel.Stable, "Stable — مستقر"),
            new ChannelOption(UpdateChannel.Beta, "Beta — تجريبي")
        };
        _channel.DisplayMemberPath = nameof(ChannelOption.Label);
        _channel.SelectedIndex = 0;
        _channel.Height = 34;
        _channel.Background = Brush("#0A1B2D");
        _channel.Foreground = Brush("#EAF3FF");
        _channel.BorderBrush = Brush("#35516F");
        Grid.SetColumn(_channel, 1);
        filters.Children.Add(_channel);

        _checkButton.Margin = new Thickness(10, 0, 6, 0);
        Grid.SetColumn(_checkButton, 2);
        filters.Children.Add(_checkButton);

        _openButton.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(_openButton, 3);
        filters.Children.Add(_openButton);

        DockPanel.SetDock(filters, Dock.Top);
        root.Children.Add(filters);

        var packageActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        packageActions.Children.Add(_trustKeyButton);
        _assets.Width = 340;
        _assets.Height = 36;
        _assets.Margin = new Thickness(5);
        _assets.Background = Brush("#0A1B2D");
        _assets.Foreground = Brush("#EAF3FF");
        _assets.BorderBrush = Brush("#35516F");
        _assets.DisplayMemberPath = nameof(ReleaseAssetInfo.Name);
        packageActions.Children.Add(_assets);
        packageActions.Children.Add(_stageButton);
        packageActions.Children.Add(_installButton);
        packageActions.Children.Add(_rollbackButton);
        DockPanel.SetDock(packageActions, Dock.Top);
        root.Children.Add(packageActions);

        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.Wrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.Background = Brush("#071625");
        _details.Foreground = Brush("#DCEBFA");
        _details.BorderBrush = Brush("#263D58");
        _details.Padding = new Thickness(12);
        _details.FontFamily = new FontFamily("Consolas");
        _details.FontSize = 10;
        _details.Text = "اختر قناة Stable أو Beta ثم اضغط فحص الإصدارات.";
        root.Children.Add(_details);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Text = "لم يتم الاتصال بصفحة Releases بعد.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(_status);

        _progress.Height = 4;
        _progress.IsIndeterminate = true;
        _progress.Visibility = Visibility.Collapsed;
        _progress.Foreground = Brush("#38BDF8");
        footer.Children.Add(_progress);

        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        return root;
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;

        var channel = _channel.SelectedItem is ChannelOption option
            ? option.Channel
            : UpdateChannel.Stable;
        _operation = new CancellationTokenSource();
        SetBusy(true, "جارٍ قراءة بيانات Releases الرسمية من GitHub...");
        try
        {
            _lastResult = await _service.CheckAsync(CurrentVersion, channel, _operation.Token);
            RenderResult(_lastResult);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء فحص الإصدارات.";
        }
        catch (Exception ex)
        {
            _lastResult = null;
            _openButton.IsEnabled = false;
            _details.Text = "UPDATE CHECK FAILED\n" + ex;
            _status.Text = "تعذر فحص الإصدارات: " + ex.Message;
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false, _status.Text);
        }
    }

    private void RenderResult(ReleaseUpdateResult result)
    {
        _status.Text = result.Message;
        _openButton.IsEnabled = result.LatestRelease is not null &&
                                ReleaseUpdateService.IsOfficialReleaseUrl(result.LatestRelease.HtmlUrl);
        _assets.ItemsSource = result.LatestRelease?.Assets
            .Where(asset => asset.SizeBytes is > 0 and <= 1024L * 1024 * 1024)
            .ToArray() ?? [];
        _assets.SelectedItem = _assets.Items.OfType<ReleaseAssetInfo>()
            .FirstOrDefault(asset => asset.Name.Contains("Windows", StringComparison.OrdinalIgnoreCase) &&
                                     asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? _assets.Items.OfType<ReleaseAssetInfo>().FirstOrDefault();
        _stageButton.IsEnabled = result.UpdateAvailable && _assets.SelectedItem is ReleaseAssetInfo;

        if (result.LatestRelease is null)
        {
            _details.Text = $"CURRENT VERSION: {result.CurrentVersion}\nCHANNEL: {result.Channel}\nCHECKED UTC: {result.CheckedAtUtc:O}\n\n{result.Message}";
            return;
        }

        var release = result.LatestRelease;
        var output = new StringBuilder();
        output.AppendLine("CURRENT VERSION: " + result.CurrentVersion);
        output.AppendLine("CHANNEL: " + result.Channel);
        output.AppendLine("LATEST RELEASE: " + release.TagName);
        output.AppendLine("PRERELEASE: " + release.IsPrerelease);
        output.AppendLine("PUBLISHED UTC: " + (release.PublishedAt?.ToString("O") ?? "Unknown"));
        output.AppendLine("UPDATE AVAILABLE: " + result.UpdateAvailable);
        output.AppendLine("RELEASE PAGE: " + release.HtmlUrl);
        output.AppendLine();
        output.AppendLine("RELEASE NOTES:");
        output.AppendLine(string.IsNullOrWhiteSpace(release.Body) ? "(No release notes provided.)" : release.Body);
        output.AppendLine();
        output.AppendLine("RELEASE ASSETS (METADATA ONLY — NOT DOWNLOADED):");
        foreach (var asset in release.Assets)
        {
            output.AppendLine($"- {asset.Name} | {asset.SizeBytes:N0} bytes");
            output.AppendLine("  " + asset.DownloadUrl);
        }
        output.AppendLine();
        output.AppendLine("SECURITY: Windows package download is enabled only after importing and trusting the independent SoftBox Release Publisher RSA public key.");
        output.AppendLine("SIGNATURE: The downloaded archive, publisher identity and every file SHA-256 are verified before staging.");
        output.AppendLine("INSTALL: Scheduled after app exit with a full previous-install snapshot and explicit rollback.");
        _details.Text = output.ToString();
    }

    private async void TrustUpdatePublisherKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "اختيار مفتاح SoftBox Release Publisher العام",
            Filter = "RSA public key (*.pem;*.pub)|*.pem;*.pub|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var pem = await File.ReadAllTextAsync(dialog.FileName);
            if (pem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("استورد المفتاح العام فقط؛ لا تختَر المفتاح الخاص.");
            var trusted = await _trustStore.TrustPublicKeyAsync("SoftBox Release Publisher", pem);
            _status.Text = "تم حفظ مفتاح إصدار SoftBox مشفرًا لهذا المستخدم. Key ID: " + trusted.KeyId;
            _stageButton.IsEnabled = _lastResult?.UpdateAvailable == true && _assets.SelectedItem is ReleaseAssetInfo;
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر استيراد مفتاح التحديث: " + ex.Message;
        }
    }

    private async void StageUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult?.UpdateAvailable != true || _lastResult.LatestRelease is null ||
            _assets.SelectedItem is not ReleaseAssetInfo asset)
        {
            _status.Text = "افحص وجود إصدار أحدث واختر ملف Windows أولًا.";
            return;
        }
        if (_operation is not null)
            return;

        _operation = new CancellationTokenSource();
        SetBusy(true, "جارٍ تنزيل حزمة Windows الرسمية والتحقق من توقيعها وبصمات ملفاتها...");
        _installButton.IsEnabled = false;
        try
        {
            _stagedUpdate = await _packageService.DownloadAndStageAsync(
                asset, _lastResult.LatestRelease.TagName, _operation.Token);
            var output = new StringBuilder();
            output.AppendLine("SIGNED WINDOWS UPDATE VERIFIED");
            output.AppendLine("VERSION: " + _stagedUpdate.Version);
            output.AppendLine("PUBLISHER: " + _stagedUpdate.Publisher);
            output.AppendLine("STAGED DIRECTORY: " + _stagedUpdate.StageDirectory);
            output.AppendLine("MAIN EXECUTABLE: " + _stagedUpdate.MainExecutable);
            output.AppendLine();
            foreach (var file in _stagedUpdate.Files)
                output.AppendLine($"{file.Path} | {file.SizeBytes:N0} bytes | SHA256 {file.Sha256}");
            output.AppendLine();
            output.AppendLine("لم يتم استبدال ملفات البرنامج بعد. الخطوة التالية تحتاج موافقتك وإغلاق SoftBox.");
            _details.Text = output.ToString();
            _installButton.IsEnabled = true;
            _status.Text = "نجح التحقق من الناشر ومحتويات الحزمة. التحديث جاهز للتثبيت بعد إغلاق التطبيق.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء تنزيل وتجهيز التحديث.";
        }
        catch (Exception ex)
        {
            _stagedUpdate = null;
            _status.Text = "رفض SoftBox حزمة التحديث أو تعذر تجهيزها: " + ex.Message;
            _details.Text = ex.ToString();
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _checkButton.IsEnabled = true;
            _channel.IsEnabled = true;
            _progress.Visibility = Visibility.Collapsed;
        }
    }

    private void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_stagedUpdate is null || !Directory.Exists(_stagedUpdate.StageDirectory))
        {
            _status.Text = "لا توجد حزمة موقعة ومجهزة للتثبيت.";
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) ||
            !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "التثبيت الذاتي متاح فقط من نسخة Windows المنشورة كملف EXE مستقل، وليس من جلسة dotnet development.";
            return;
        }

        try
        {
            var script = _packageService.BuildInstallScript(
                _stagedUpdate,
                AppContext.BaseDirectory,
                executable,
                Environment.ProcessId,
                CurrentVersion);
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(script);
            Process.Start(start) ?? throw new InvalidOperationException("تعذر بدء مساعد التحديث.");
            _status.Text = "بدأ مساعد التحديث. سيتم إغلاق SoftBox، ثم حفظ النسخة السابقة والتحقق مجددًا قبل استبدال الملفات.";
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر جدولة تثبيت التحديث: " + ex.Message;
        }
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _rollbackState = await _packageService.ReadRollbackStateAsync();
            if (_rollbackState is null)
            {
                _status.Text = "لا توجد نسخة سابقة محفوظة للرجوع إليها.";
                _rollbackButton.IsEnabled = false;
                return;
            }
            if (MessageBox.Show(this,
                    $"سيتم إغلاق SoftBox والرجوع من {_rollbackState.InstalledVersion} إلى {_rollbackState.PreviousVersion}. هل تتابع؟",
                    "تأكيد الرجوع إلى النسخة السابقة",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var script = _packageService.BuildRollbackScript(_rollbackState, Environment.ProcessId);
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(script);
            Process.Start(start) ?? throw new InvalidOperationException("تعذر بدء مساعد الرجوع.");
            _status.Text = "بدأ مساعد الرجوع. سيتم إغلاق SoftBox واستعادة نسخة الملفات السابقة.";
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تنفيذ الرجوع: " + ex.Message;
        }
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        var url = _lastResult?.LatestRelease?.HtmlUrl;
        if (!ReleaseUpdateService.IsOfficialReleaseUrl(url))
        {
            _status.Text = "رابط الإصدار غير موجود أو غير موثوق.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
            _status.Text = "تم طلب فتح صفحة الإصدار الرسمية في المتصفح. لم يتم تنزيل أو تثبيت أي ملف.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح المتصفح: " + ex.Message;
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _checkButton.IsEnabled = !busy;
        _channel.IsEnabled = !busy;
        _openButton.IsEnabled = !busy && _lastResult?.LatestRelease is not null;
        _trustKeyButton.IsEnabled = !busy;
        _stageButton.IsEnabled = !busy && _lastResult?.UpdateAvailable == true && _assets.SelectedItem is ReleaseAssetInfo;
        _assets.IsEnabled = !busy;
        _rollbackButton.IsEnabled = !busy && _rollbackState is not null;
    }

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            MinWidth = 125,
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

    private sealed record ChannelOption(UpdateChannel Channel, string Label);
}
