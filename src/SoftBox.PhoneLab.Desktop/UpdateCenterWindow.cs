using SoftBox.PhoneLab.Core.Updates;
using System.Diagnostics;
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
    private readonly ComboBox _channel = new();
    private readonly Button _checkButton;
    private readonly Button _openButton;
    private readonly TextBox _details = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private ReleaseUpdateResult? _lastResult;
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

        _checkButton = ButtonControl("فحص الإصدارات", "#14517A", Check_Click);
        _openButton = ButtonControl("فتح صفحة الإصدار الرسمية", "#176044", OpenRelease_Click);
        _openButton.IsEnabled = false;
        Content = BuildContent();
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
                Text = "هذه الخدمة تقرأ بيانات الإصدارات العامة من GitHub فقط. لا تتحقق من توقيع ملفات Windows ولا تنزّلها أو تثبّتها. لا تشغّل أي ملف من صفحة إصدار قبل التحقق من مصدره وتوقيعه يدويًا.",
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
        output.AppendLine("SECURITY: Publisher signature and binary authenticity have not been verified by this checker.");
        _details.Text = output.ToString();
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
