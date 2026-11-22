using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

public sealed class BoardMapCsvImportWindow : Window
{
    private readonly BoardMapCatalog _catalog;
    private readonly DeviceSnapshot _device;
    private readonly string _csvPath;
    private readonly TextBox _sourceName = Field();
    private readonly TextBox _sourceUri = Field();
    private readonly TextBox _sourceDocument = Field();
    private readonly TextBox _sourceLicense = Field();
    private readonly TextBox _reviewer = Field();
    private readonly TextBox _hardwareRevision = Field();
    private readonly TextBox _boardRevision = Field();
    private readonly TextBox _width = Field();
    private readonly TextBox _height = Field();
    private readonly CheckBox _confirmRevision = new();
    private readonly TextBlock _status = new();
    private readonly Button _import;
    public string? ImportedPath { get; private set; }

    public BoardMapCsvImportWindow(
        BoardMapCatalog catalog,
        DeviceSnapshot device,
        string csvPath)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _csvPath = Path.GetFullPath(csvPath);

        Title = "SoftBox | استيراد BoardView CSV موثّق";
        Width = 820;
        Height = 790;
        MinWidth = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _sourceName.Text = "";
        _sourceUri.Text = "";
        _sourceDocument.Text = "";
        _sourceLicense.Text = "";
        _reviewer.Text = Environment.UserName;
        _hardwareRevision.Text = _device.HardwareRevision == "غير متاح" ? "" : _device.HardwareRevision;
        _boardRevision.Text = _device.BoardRevision == "غير متاح" ? "" : _device.BoardRevision;
        _width.Text = "65";
        _height.Text = "130";
        _confirmRevision.Content = new TextBlock
        {
            Text = "أكدت يدويًا أن رقم الموديل ومراجعة اللوحة الفيزيائية مطابقان لمصدر BoardView، وأن الإحداثيات بالمليمتر.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#F0C9A5")
        };
        _confirmRevision.Foreground = Brush("#F0C9A5");
        _import = ButtonControl("تحقق واستيراد الخريطة", "#176044", Import_Click);
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "بيانات المصدر ومراجعة اللوحة",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = $"الجهاز المحدد: {_device.Manufacturer} {_device.Model} • {_device.ModelNumber} • {_device.DeviceCode}\nCSV: {Path.GetFileName(_csvPath)}",
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 6, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });
        header.Children.Add(new TextBlock
        {
            Text = "لن يتم استيراد الخريطة إذا لم تؤكد المراجعة الفيزيائية. بيانات المصدر والترخيص يجب أن تكون حقيقية؛ SHA-256 يحسبه SoftBox لملف CSV الفعلي.",
            Foreground = Brush("#F0C9A5"),
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        for (var i = 0; i < 9; i++) fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddField(fields, 0, "اسم المصدر", _sourceName);
        AddField(fields, 1, "رابط المصدر HTTPS", _sourceUri);
        AddField(fields, 2, "معرّف المستند", _sourceDocument);
        AddField(fields, 3, "الترخيص / الإذن", _sourceLicense);
        AddField(fields, 4, "اسم الفني المراجع", _reviewer);
        AddField(fields, 5, "Hardware Revision (اختياري إذا غير معروف)", _hardwareRevision);
        AddField(fields, 6, "Board Revision (لازم)", _boardRevision);
        AddField(fields, 7, "عرض اللوحة بالمليمتر", _width);
        AddField(fields, 8, "طول اللوحة بالمليمتر", _height);
        root.Children.Add(fields);

        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        footer.Children.Add(_confirmRevision);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_import);
        buttons.Children.Add(ButtonControl("إلغاء", "#51333A", (_, _) => { this.SetResult(false); }));
        footer.Children.Add(buttons);
        _status.Text = "صيغة CSV المطلوبة موضحة في docs/BOARDVIEW-IMPORT.md.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 0);
        footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        return root;
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(_width.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) ||
            !double.TryParse(_height.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
        {
            _status.Text = "أدخل أبعادًا رقمية صحيحة بالمليمتر.";
            return;
        }
        if (!_confirmRevision.IsChecked.GetValueOrDefault())
        {
            _status.Text = "يجب تأكيد مطابقة مراجعة اللوحة قبل الاستيراد.";
            return;
        }

        _import.IsEnabled = false;
        try
        {
            var metadata = new BoardMapCsvImportMetadata(
                _sourceName.Text, _sourceUri.Text, _sourceDocument.Text, _sourceLicense.Text,
                _reviewer.Text, _hardwareRevision.Text, _boardRevision.Text, width, height,
                "boardview", RevisionMatchConfirmed: true);
            ImportedPath = await _catalog.ImportCsvMapAsync(_csvPath, _device, metadata);
            this.SetResult(true);
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = "رفض الاستيراد: " + ex.Message;
            _import.IsEnabled = true;
        }
    }

    private static void AddField(Grid grid, int row, string label, TextBox field)
    {
        var title = new TextBlock { Text = label, Foreground = Brush("#C4D5E8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 8, 6) };
        Grid.SetRow(title, row);
        Grid.SetColumn(title, 0);
        grid.Children.Add(title);
        field.Margin = new Thickness(3, 5, 0, 5);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static TextBox Field() => new()
    {
        Padding = new Thickness(8),
        Background = Brush("#071625"),
        Foreground = Brush("#DCEBFA"),
        BorderBrush = Brush("#38536F"),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 120,
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
