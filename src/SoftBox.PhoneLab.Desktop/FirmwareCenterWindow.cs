using System.IO;
using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Firmware;
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
    private readonly ObservableCollection<FirmwareLibraryItem> _items = [];
    private readonly ListBox _list = new();
    private readonly TextBox _manufacturer = new();
    private readonly TextBox _model = new();
    private readonly TextBox _region = new();
    private readonly TextBox _version = new();
    private readonly TextBox _source = new();
    private readonly TextBox _notes = new();
    private readonly TextBox _details = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _importButton;
    private readonly Button _verifyButton;
    private readonly Button _inspectButton;
    private CancellationTokenSource? _operation;

    public FirmwareCenterWindow()
    {
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

        Content = BuildContent();
        _list.SelectionChanged += (_, _) => ShowSelected();
        Loaded += async (_, _) => await LoadItemsAsync();
        Closed += (_, _) => _operation?.Cancel();
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
            Text = "OFFLINE FIRMWARE LIBRARY • LOCAL IMPORT • SHA-256 REVALIDATION",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "يستورد ملفات Firmware التي تختارها من الكمبيوتر إلى مكتبة محلية مصنفة حسب الشركة والموديل والمنطقة والإصدار. يعيد حساب SHA-256 للتحقق من عدم تغير الملف منذ الاستيراد. تطابق البصمة لا يثبت الناشر أو توافق الملف مع الهاتف.",
            TextWrapping = TextWrapping.Wrap,
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
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock
            {
                Text = "لا يوجد تنزيل تلقائي من مصادر خارجية في هذه النسخة، ولا توجد عملية تفليش. لا تستخدم الملف لمجرد تطابق SHA-256 المحلي؛ يلزم التحقق من مصدر رسمي وطراز الجهاز والمنطقة وقيود الإصدار قبل أي استعادة.",
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

        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        buttons.Children.Add(_importButton);
        buttons.Children.Add(_verifyButton);
        buttons.Children.Add(_inspectButton);
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
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _importButton.IsEnabled = !busy;
        _verifyButton.IsEnabled = !busy;
        _inspectButton.IsEnabled = !busy;
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
