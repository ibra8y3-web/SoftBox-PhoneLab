using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Offline;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Read-only inventory of the actual offline dependencies and data shipped beside SoftBox.
/// Missing files remain visibly missing; this window never downloads or installs them.
/// </summary>
public sealed class OfflineCenterWindow : Window
{
    private readonly OfflineReadinessService _service;
    private readonly ObservableCollection<OfflinePackageStatus> _packages = [];
    private readonly ObservableCollection<OfflineDataDirectoryStatus> _data = [];
    private readonly DataGrid _packagesGrid = new();
    private readonly DataGrid _dataGrid = new();
    private readonly TextBlock _summary = new();
    private readonly TextBlock _details = new();
    private readonly Button _refreshButton;
    private readonly Button _exportButton;
    private OfflineReadinessSnapshot? _snapshot;
    private CancellationTokenSource? _scan;

    public OfflineCenterWindow(OfflineReadinessService? service = null)
    {
        _service = service ?? new OfflineReadinessService();
        Title = "SoftBox | مركز العمل دون إنترنت — Offline Center";
        Width = 1220;
        Height = 820;
        MinWidth = 940;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _refreshButton = ButtonControl("فحص الحزمة المحلية", "#14517A", Refresh_Click);
        _exportButton = ButtonControl("تصدير تقرير JSON", "#176044", Export_Click);
        Content = BuildContent();
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _scan?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "مركز العمل دون إنترنت",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "LOCAL OFFLINE READINESS • BUNDLED TOOLS • LOCAL DATA • SHA-256",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "يقرأ الملفات والمجلدات الموجودة داخل حزمة SoftBox الحالية فقط. لا يقوم بتنزيل أو تثبيت أو تشغيل الأدوات، ولا يعتبر وجود واجهة شبكة دليلًا على توفر الإنترنت.",
            FontSize = 11,
            Foreground = Brush("#B7C9DE"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _summary.Foreground = Brush("#9FC7E8");
        _summary.TextWrapping = TextWrapping.Wrap;
        _summary.Margin = new Thickness(0, 0, 0, 5);
        footer.Children.Add(_summary);
        _details.Foreground = Brush("#8199B5");
        _details.FontSize = 10;
        _details.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(_details);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        toolbar.Children.Add(_refreshButton);
        _exportButton.Margin = new Thickness(8, 0, 0, 0);
        _exportButton.IsEnabled = false;
        toolbar.Children.Add(_exportButton);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var tabs = new TabControl { Background = Brush("#0D1C2E"), BorderBrush = Brush("#263D58") };
        tabs.Items.Add(BuildPackagesTab());
        tabs.Items.Add(BuildDataTab());
        root.Children.Add(tabs);
        return root;
    }

    private TabItem BuildPackagesTab()
    {
        _packagesGrid.AutoGenerateColumns = false;
        _packagesGrid.IsReadOnly = true;
        _packagesGrid.ItemsSource = _packages;
        _packagesGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _packagesGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _packagesGrid.Background = Brush("#0A1828");
        _packagesGrid.Foreground = Brush("#DCEBFA");
        _packagesGrid.RowBackground = Brush("#102238");
        _packagesGrid.AlternatingRowBackground = Brush("#0C1C30");
        _packagesGrid.BorderBrush = Brush("#263D58");
        _packagesGrid.Columns.Add(TextColumn("المكوّن", nameof(OfflinePackageStatus.NameAr), 175));
        _packagesGrid.Columns.Add(TextColumn("الفئة", nameof(OfflinePackageStatus.Category), 120));
        _packagesGrid.Columns.Add(TextColumn("الحالة", nameof(OfflinePackageStatus.AvailabilityLabelAr), 125));
        _packagesGrid.Columns.Add(TextColumn("الحجم", nameof(OfflinePackageStatus.SizeLabel), 100));
        _packagesGrid.Columns.Add(TextColumn("الإصدار", nameof(OfflinePackageStatus.Version), 120));
        _packagesGrid.Columns.Add(TextColumn("المسار داخل الحزمة", nameof(OfflinePackageStatus.RelativePath), 220));
        _packagesGrid.Columns.Add(TextColumn("SHA-256", nameof(OfflinePackageStatus.Sha256), 360));
        return new TabItem { Header = "الأدوات والملفات المرفقة", Content = _packagesGrid };
    }

    private TabItem BuildDataTab()
    {
        _dataGrid.AutoGenerateColumns = false;
        _dataGrid.IsReadOnly = true;
        _dataGrid.ItemsSource = _data;
        _dataGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _dataGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _dataGrid.Background = Brush("#0A1828");
        _dataGrid.Foreground = Brush("#DCEBFA");
        _dataGrid.RowBackground = Brush("#102238");
        _dataGrid.AlternatingRowBackground = Brush("#0C1C30");
        _dataGrid.BorderBrush = Brush("#263D58");
        _dataGrid.Columns.Add(TextColumn("مجلد البيانات", nameof(OfflineDataDirectoryStatus.NameAr), 210));
        _dataGrid.Columns.Add(TextColumn("الحالة", nameof(OfflineDataDirectoryStatus.StatusLabelAr), 130));
        _dataGrid.Columns.Add(TextColumn("عدد الملفات", nameof(OfflineDataDirectoryStatus.FileCount), 120));
        _dataGrid.Columns.Add(TextColumn("الحجم", nameof(OfflineDataDirectoryStatus.SizeLabel), 120));
        _dataGrid.Columns.Add(TextColumn("المسار", nameof(OfflineDataDirectoryStatus.RelativePath), 260));
        _dataGrid.Columns.Add(TextColumn("التفاصيل", nameof(OfflineDataDirectoryStatus.Details), 360));
        return new TabItem { Header = "قواعد البيانات المحلية", Content = _dataGrid };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        _scan?.Cancel();
        _scan?.Dispose();
        _scan = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var snapshot = await _service.GetSnapshotAsync(_scan.Token);
            _snapshot = snapshot;

            _packages.Clear();
            foreach (var package in snapshot.Packages)
                _packages.Add(package);
            _data.Clear();
            foreach (var directory in snapshot.DataDirectories)
                _data.Add(directory);

            _summary.Text =
                $"المكوّنات المتاحة: {snapshot.AvailablePackageCount} / {snapshot.Packages.Count} • " +
                $"غير الموجودة: {snapshot.MissingPackageCount} • تعذر قراءتها: {snapshot.UnreadablePackageCount} • " +
                $"{snapshot.NetworkStatusLabel} • {snapshot.StorageAvailableLabel}";
            _details.Text =
                $"التطبيق: {snapshot.ApplicationVersion} • النظام: {snapshot.OperatingSystem} • " +
                $"Runtime: {snapshot.Runtime} • وقت الفحص UTC: {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss} • " +
                $"المجلد: {snapshot.ApplicationRoot}";
            _exportButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            _summary.Text = "تم إلغاء الفحص.";
        }
        catch (Exception ex)
        {
            _summary.Text = "تعذر فحص المكونات المحلية: " + ex.Message;
            _summary.Foreground = Brush("#FCA5A5");
            _exportButton.IsEnabled = false;
        }
        finally
        {
            if (IsVisible)
                SetBusy(false);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "تصدير تقرير الجاهزية المحلية",
            Filter = "JSON report (*.json)|*.json",
            FileName = $"SoftBox-Offline-Readiness-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var json = JsonSerializer.Serialize(_snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(dialog.FileName, json, CancellationToken.None);
            _summary.Text = "تم تصدير التقرير المحلي إلى: " + dialog.FileName;
            _summary.Foreground = Brush("#86EFAC");
        }
        catch (Exception ex)
        {
            _summary.Text = "تعذر تصدير التقرير: " + ex.Message;
            _summary.Foreground = Brush("#FCA5A5");
        }
    }

    private void SetBusy(bool busy)
    {
        _refreshButton.IsEnabled = !busy;
        _exportButton.IsEnabled = !busy && _snapshot is not null;
        if (busy)
        {
            _summary.Foreground = Brush("#9FC7E8");
            _summary.Text = "جارٍ فحص الملفات المحلية وحساب بصمات SHA-256...";
        }
    }

    private static DataGridTextColumn TextColumn(string header, string path, double width)
        => new()
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path),
            Width = width
        };

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            MinWidth = 150,
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
}
