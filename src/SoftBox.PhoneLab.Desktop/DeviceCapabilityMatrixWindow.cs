using SoftBox.PhoneLab.Desktop.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

public sealed class DeviceCapabilityMatrixWindow : Window
{
    private readonly DeviceCapabilityMatrixSnapshot _report;
    private readonly DataGrid _grid = new();

    public DeviceCapabilityMatrixWindow(DeviceCapabilityMatrixSnapshot report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        Title = "SoftBox | مصفوفة قدرات وتوافق الجهاز";
        Width = 1220;
        Height = 780;
        MinWidth = 950;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "مصفوفة القدرات والتوافق",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{_report.DeviceName} • {_report.Manufacturer} • Model: {_report.ModelNumber} • Code: {_report.DeviceCode}",
            Foreground = Brush("#D5E9FA"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 3)
        });
        header.Children.Add(new TextBlock
        {
            Text = $"SoC: {_report.Soc} • Android: {_report.AndroidVersion} • API: {_report.ApiLevel}",
            Foreground = Brush("#9FC7E8"),
            Margin = new Thickness(0, 2, 0, 8)
        });
        header.Children.Add(new TextBlock
        {
            Text = "تعرض النافذة الأدلة التي قرأها SoftBox لهذا الجهاز فقط. «غير معروف» تعني أن البروتوكول أو التوافق لم يتم اختباره، وليست موافقة على التفليش أو ضمانًا للدعم.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#FCD38D"),
            Margin = new Thickness(0, 3, 0, 8)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = false;
        _grid.IsReadOnly = true;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _grid.Background = Brush("#0D1C2E");
        _grid.Foreground = Brush("#EAF3FF");
        _grid.RowBackground = Brush("#12243A");
        _grid.AlternatingRowBackground = Brush("#172B43");
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _grid.Columns.Add(Column("القدرة", nameof(DeviceCapabilityAssessment.CapabilityName), 1.1));
        _grid.Columns.Add(Column("الحالة", nameof(DeviceCapabilityAssessment.StatusLabel), .7));
        _grid.Columns.Add(Column("الدليل المتاح", nameof(DeviceCapabilityAssessment.Evidence), 1.8));
        _grid.Columns.Add(Column("القيود / ما لا يثبته الدليل", nameof(DeviceCapabilityAssessment.Limitation), 1.8));
        _grid.ItemsSource = _report.Capabilities;
        root.Children.Add(_grid);

        var footer = new TextBlock
        {
            Text = $"وقت التقييم: {_report.EvaluatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC • لا يتم استنتاج دعم Qualcomm EDL أو MediaTek BROM أو Fastboot أو Firmware من قراءة ADB وحدها.",
            Foreground = Brush("#91A7C1"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        return root;
    }

    private static DataGridTextColumn Column(string header, string property, double weight)
        => new()
        {
            Header = header,
            Binding = new Binding(property),
            Width = new DataGridLength(weight, DataGridLengthUnitType.Star),
            IsReadOnly = true
        };

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
