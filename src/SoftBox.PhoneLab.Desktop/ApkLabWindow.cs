using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Apk;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Local APK inspection workspace. It never installs, modifies or uploads the selected APK.
/// </summary>
public sealed class ApkLabWindow : Window
{
    private readonly ApkInspectionService _inspector = new();
    private readonly TextBox _path = new();
    private readonly TextBox _comparisonPath = new();
    private readonly TextBox _summary = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _inspectButton;
    private readonly Button _compareButton;
    private readonly Button _cancelButton;
    private readonly ObservableCollection<string> _permissions = [];
    private readonly ObservableCollection<string> _entries = [];
    private readonly ObservableCollection<string> _signatureFiles = [];
    private readonly ObservableCollection<string> _warnings = [];
    private readonly ObservableCollection<string> _comparisonResults = [];
    private CancellationTokenSource? _operation;

    public ApkLabWindow()
    {
        Title = "SoftBox | مختبر APK — APK Lab";
        Width = 1040;
        Height = 780;
        MinWidth = 820;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _inspectButton = CreateButton("تحليل ملف APK", "#14517A", Inspect_Click);
        _compareButton = CreateButton("مقارنة الإصدارين", "#176044", Compare_Click);
        _cancelButton = CreateButton("إلغاء", "#51333A", Cancel_Click);
        _cancelButton.IsEnabled = false;

        Content = BuildContent();
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "مختبر ملفات APK",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "APK LAB • LOCAL READ-ONLY INSPECTION",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "تحليل محلي لبنية ZIP وAndroidManifest.xml والصلاحيات وبصمة SHA-256. لا يتم تثبيت التطبيق أو تعديله أو رفعه إلى الإنترنت.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 9, 0, 0),
            FontSize = 12
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Border
        {
            Background = Brush("#211D2A"),
            BorderBrush = Brush("#66516B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 12, 0, 0),
            Child = new TextBlock
            {
                Text = "تنبيه أمني: قراءة Manifest والصلاحيات ووجود ملفات التوقيع لا تثبت أن التطبيق آمن أو موثوق. هذه النسخة لا تنفذ تحققًا تشفيريًا من شهادات APK ولا تفك موارد التطبيق ولا تثبته على الهاتف.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _path.Height = 38;
        _path.Margin = new Thickness(0, 0, 8, 0);
        _path.Padding = new Thickness(10, 7, 10, 7);
        _path.Background = Brush("#0D1C2E");
        _path.Foreground = Brush("#EAF3FF");
        _path.BorderBrush = Brush("#35516F");
        _path.VerticalContentAlignment = VerticalAlignment.Center;
        _path.ToolTip = "المسار المحلي لملف APK";
        Grid.SetColumn(_path, 0);
        toolbar.Children.Add(_path);

        var browse = CreateButton("اختيار APK", "#243B56", Browse_Click);
        browse.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(browse, 1);
        toolbar.Children.Add(browse);

        _inspectButton.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(_inspectButton, 2);
        toolbar.Children.Add(_inspectButton);

        Grid.SetColumn(_cancelButton, 3);
        toolbar.Children.Add(_cancelButton);

        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var comparisonToolbar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        comparisonToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        comparisonToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        comparisonToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _comparisonPath.Height = 36;
        _comparisonPath.Margin = new Thickness(0, 0, 8, 0);
        _comparisonPath.Padding = new Thickness(10, 6, 10, 6);
        _comparisonPath.Background = Brush("#0D1C2E");
        _comparisonPath.Foreground = Brush("#EAF3FF");
        _comparisonPath.BorderBrush = Brush("#35516F");
        _comparisonPath.VerticalContentAlignment = VerticalAlignment.Center;
        _comparisonPath.ToolTip = "اختر ملف APK الثاني للمقارنة مع الملف الأول";
        Grid.SetColumn(_comparisonPath, 0);
        comparisonToolbar.Children.Add(_comparisonPath);

        var browseComparison = CreateButton("اختيار APK الثاني", "#243B56", BrowseComparison_Click);
        browseComparison.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(browseComparison, 1);
        comparisonToolbar.Children.Add(browseComparison);

        _compareButton.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(_compareButton, 2);
        comparisonToolbar.Children.Add(_compareButton);

        DockPanel.SetDock(comparisonToolbar, Dock.Top);
        root.Children.Add(comparisonToolbar);

        var statePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        _status.Text = "اختر ملف APK لبدء الفحص المحلي.";
        _status.Foreground = Brush("#9FC7E8");
        _status.FontSize = 11;
        _status.Margin = new Thickness(0, 0, 0, 6);
        statePanel.Children.Add(_status);

        _progress.Height = 4;
        _progress.IsIndeterminate = true;
        _progress.Visibility = Visibility.Collapsed;
        _progress.Foreground = Brush("#38BDF8");
        statePanel.Children.Add(_progress);
        DockPanel.SetDock(statePanel, Dock.Top);
        root.Children.Add(statePanel);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _summary.IsReadOnly = true;
        _summary.AcceptsReturn = true;
        _summary.TextWrapping = TextWrapping.Wrap;
        _summary.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _summary.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _summary.Background = Brush("#0D1C2E");
        _summary.Foreground = Brush("#DCEBFA");
        _summary.BorderBrush = Brush("#263D58");
        _summary.Padding = new Thickness(12);
        _summary.FontFamily = new FontFamily("Consolas");
        _summary.FontSize = 11;
        _summary.Text = "ستظهر بيانات الحزمة وManifest والبصمة هنا بعد الفحص.";
        Grid.SetRow(_summary, 0);
        body.Children.Add(_summary);

        var tabs = new TabControl
        {
            Margin = new Thickness(0, 12, 0, 0),
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58")
        };
        tabs.Items.Add(CreateTab("الصلاحيات", _permissions));
        tabs.Items.Add(CreateTab("ملفات الأرشيف", _entries));
        tabs.Items.Add(CreateTab("بيانات التوقيع", _signatureFiles));
        tabs.Items.Add(CreateTab("مقارنة إصدارين", _comparisonResults));
        tabs.Items.Add(CreateTab("التحذيرات والحدود", _warnings));
        Grid.SetRow(tabs, 1);
        body.Children.Add(tabs);

        root.Children.Add(body);
        return root;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار ملف Android APK",
            Filter = "Android Package (*.apk)|*.apk|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _path.Text = dialog.FileName;
            _status.Text = "تم اختيار الملف. اضغط «تحليل ملف APK» لبدء الفحص.";
        }
    }

    private void BrowseComparison_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار ملف APK الثاني للمقارنة",
            Filter = "Android Package (*.apk)|*.apk|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _comparisonPath.Text = dialog.FileName;
            _status.Text = "تم اختيار الملف الثاني. اختر الملف الأول ثم ابدأ مقارنة الإصدارين.";
        }
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;

        if (string.IsNullOrWhiteSpace(_path.Text))
        {
            MessageBox.Show(this, "اختر ملف APK أولًا.", "APK Lab",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _operation = new CancellationTokenSource();
        _inspectButton.IsEnabled = false;
        _compareButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        _progress.Visibility = Visibility.Visible;
        _status.Text = "جارٍ حساب SHA-256 وقراءة جميع مدخلات الأرشيف وتحليل Manifest...";
        try
        {
            var result = await _inspector.InspectAsync(_path.Text.Trim(), _operation.Token);
            DisplayResult(result);
            _status.Text = "اكتمل قراءة الأرشيف المحلي. راجع التحذيرات وحدود التحليل قبل اتخاذ أي قرار.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء الفحص.";
        }
        catch (Exception ex)
        {
            _status.Text = "فشل فحص ملف APK.";
            MessageBox.Show(this, ex.Message, "تعذر تحليل APK",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _inspectButton.IsEnabled = true;
            _compareButton.IsEnabled = true;
            _cancelButton.IsEnabled = false;
            _progress.Visibility = Visibility.Collapsed;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null)
            return;

        if (string.IsNullOrWhiteSpace(_path.Text) || string.IsNullOrWhiteSpace(_comparisonPath.Text))
        {
            MessageBox.Show(this, "اختر ملف APK الأول وملف APK الثاني قبل المقارنة.",
                "APK Lab", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _operation = new CancellationTokenSource();
        _inspectButton.IsEnabled = false;
        _compareButton.IsEnabled = false;
        _cancelButton.IsEnabled = true;
        _progress.Visibility = Visibility.Visible;
        _status.Text = "جارٍ تحليل الملفين محليًا ومقارنة بيانات Manifest والصلاحيات وأسماء الملفات...";
        try
        {
            var result = await _inspector.CompareAsync(
                _path.Text.Trim(),
                _comparisonPath.Text.Trim(),
                _operation.Token);
            DisplayComparison(result);
            _status.Text = result.SamePackage == false
                ? "اكتملت المقارنة، لكن اسمي الحزمة مختلفان؛ لا تعتبر الملفين إصدارين للتطبيق نفسه."
                : "اكتملت مقارنة الملفين محليًا. راجع التحذيرات وحدود المقارنة.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء مقارنة APK.";
        }
        catch (Exception ex)
        {
            _status.Text = "فشلت مقارنة ملفي APK.";
            MessageBox.Show(this, ex.Message, "تعذر مقارنة APK",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _inspectButton.IsEnabled = true;
            _compareButton.IsEnabled = true;
            _cancelButton.IsEnabled = false;
            _progress.Visibility = Visibility.Collapsed;
        }
    }

    private void DisplayComparison(ApkComparisonResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("FIRST APK: " + result.FirstFileName);
        text.AppendLine("SECOND APK: " + result.SecondFileName);
        text.AppendLine("SAME PACKAGE: " + FormatTriState(result.SamePackage));
        text.AppendLine("VERSION NAME CHANGED: " + FormatTriState(result.VersionNameChanged));
        text.AppendLine("VERSION CODE CHANGED: " + FormatTriState(result.VersionCodeChanged));
        text.AppendLine("SHA-256 IDENTICAL: " + (result.Sha256Matches ? "YES" : "NO"));
        text.AppendLine("ENTRY LISTS COMPLETE: " + (result.EntryListsComplete ? "YES" : "NO"));
        text.AppendLine("ADDED PERMISSIONS: " + result.AddedPermissions.Count);
        text.AppendLine("REMOVED PERMISSIONS: " + result.RemovedPermissions.Count);
        text.AppendLine("ADDED ENTRY NAMES: " + result.AddedArchiveEntries.Count);
        text.AppendLine("REMOVED ENTRY NAMES: " + result.RemovedArchiveEntries.Count);
        text.AppendLine("CONTENT-BY-CONTENT HASH COMPARISON: NOT PERFORMED");
        text.AppendLine("SIGNATURE VERIFICATION: NOT PERFORMED");
        _summary.Text = text.ToString();

        ReplaceItems(_permissions,
            result.AddedPermissions.Select(item => "+ " + item)
                .Concat(result.RemovedPermissions.Select(item => "- " + item)));
        ReplaceItems(_entries,
            result.AddedArchiveEntries.Select(item => "+ " + item)
                .Concat(result.RemovedArchiveEntries.Select(item => "- " + item)));
        ReplaceItems(_signatureFiles, []);
        ReplaceItems(_comparisonResults,
            new[]
            {
                "FIRST: " + result.FirstFileName,
                "SECOND: " + result.SecondFileName,
                "SAME PACKAGE: " + FormatTriState(result.SamePackage),
                "VERSION NAME CHANGED: " + FormatTriState(result.VersionNameChanged),
                "VERSION CODE CHANGED: " + FormatTriState(result.VersionCodeChanged),
                "SHA-256 IDENTICAL: " + (result.Sha256Matches ? "YES" : "NO"),
                "ENTRY LISTS COMPLETE: " + (result.EntryListsComplete ? "YES" : "NO")
            }
            .Concat(result.AddedPermissions.Select(item => "ADDED PERMISSION: " + item))
            .Concat(result.RemovedPermissions.Select(item => "REMOVED PERMISSION: " + item))
            .Concat(result.AddedArchiveEntries.Select(item => "ADDED ENTRY NAME: " + item))
            .Concat(result.RemovedArchiveEntries.Select(item => "REMOVED ENTRY NAME: " + item)));
        ReplaceItems(_warnings, result.Warnings);
    }

    private static string FormatTriState(bool? value)
        => value.HasValue ? (value.Value ? "YES" : "NO") : "UNKNOWN";

    private void DisplayResult(ApkInspectionResult result)
    {
        var text = new StringBuilder();
        text.AppendLine("FILE: " + result.FileName);
        text.AppendLine("SIZE: " + FormatBytes(result.FileSizeBytes));
        text.AppendLine("SHA-256: " + result.Sha256);
        text.AppendLine("ZIP: " + (result.ArchiveEntriesReadable
            ? $"تمت قراءة {result.EntryCount} مدخلًا دون خطأ"
            : "لم يكتمل فحص الأرشيف"));
        text.AppendLine("MANIFEST: " + result.ManifestFormat);
        text.AppendLine("PACKAGE: " + (result.PackageName ?? "غير متاح"));
        text.AppendLine("APPLICATION: " + (result.ApplicationLabel ?? "غير متاح أو مرجع مورد غير محلول"));
        text.AppendLine("VERSION NAME: " + (result.VersionName ?? "غير متاح"));
        text.AppendLine("VERSION CODE: " + (result.VersionCode?.ToString() ?? "غير متاح"));
        text.AppendLine("MIN SDK: " + (result.MinSdkVersion?.ToString() ?? "غير متاح"));
        text.AppendLine("TARGET SDK: " + (result.TargetSdkVersion?.ToString() ?? "غير متاح"));
        text.AppendLine("PERMISSIONS: " + result.Permissions.Count);
        text.AppendLine("V1 SIGNATURE FILES: " + (result.HasV1SignatureFiles ? "تم رصد ملفات" : "لم يتم رصد ملفات"));
        text.AppendLine("APK SIGNING BLOCK: " + (result.ApkSigningBlockDetected ? "تم رصد البنية" : "لم يتم رصد البنية"));
        text.AppendLine("SIGNATURE VERIFICATION: NOT PERFORMED");
        text.AppendLine("INSPECTION: READ-ONLY / LOCAL");
        _summary.Text = text.ToString();

        ReplaceItems(_permissions, result.Permissions);
        ReplaceItems(_entries, result.Entries);
        ReplaceItems(_signatureFiles, result.SignatureMetadataFiles);
        ReplaceItems(_warnings, result.Warnings);
    }

    private static void ReplaceItems(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }

    private static TabItem CreateTab(string title, IEnumerable<string> items)
    {
        var list = new ListBox
        {
            ItemsSource = items,
            Background = Brush("#0D1C2E"),
            Foreground = Brush("#DCEBFA"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            FontSize = 11
        };
        return new TabItem
        {
            Header = title,
            Content = list,
            Background = Brush("#14263A"),
            Foreground = Brush("#DCEBFA")
        };
    }

    private static Button CreateButton(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(13, 8, 13, 8),
            MinWidth = 90,
            Background = Brush(background),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Click += handler;
        return button;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
