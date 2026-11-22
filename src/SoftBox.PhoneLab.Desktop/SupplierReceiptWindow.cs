using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Desktop.Models;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Captures real supplier stock receipt details for an existing inventory SKU.
/// No stock is changed until the owning WorkshopStore commits the receipt.
/// </summary>
public sealed class SupplierReceiptWindow : Window
{
    private readonly TextBox _supplier = new();
    private readonly TextBox _invoice = new();
    private readonly TextBox _quantity = new();
    private readonly TextBox _unitCost = new();
    private readonly TextBox _technician = new();
    private readonly TextBlock _status = new();

    public string Supplier => _supplier.Text.Trim();
    public string InvoiceReference => _invoice.Text.Trim();
    public string TechnicianName => _technician.Text.Trim();
    public int Quantity { get; private set; }
    public decimal PurchaseUnitCostEgp { get; private set; }

    public SupplierReceiptWindow(InventoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Title = "SoftBox | استلام مشتريات المورد";
        Width = 560;
        Height = 600;
        MinWidth = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        Content = BuildContent(item);
    }

    private UIElement BuildContent(InventoryItem item)
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "استلام كمية من المورد",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{item.Name} | SKU: {item.Sku}\nالرصيد الحالي: {item.Quantity} | متوسط تكلفة الوحدة الحالي: {item.UnitCostEgp:0.00}",
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        AddField(panel, "اسم المورد", _supplier);
        _supplier.Text = item.Supplier;
        AddField(panel, "رقم الفاتورة / مستند الاستلام (اختياري)", _invoice);
        AddField(panel, "الكمية المستلمة", _quantity);
        _quantity.Text = "1";
        AddField(panel, "تكلفة شراء الوحدة", _unitCost);
        _unitCost.Text = item.UnitCostEgp.ToString("0.00", CultureInfo.InvariantCulture);
        AddField(panel, "اسم الموظف المستلم (اختياري)", _technician);
        _technician.Text = Environment.UserName;

        panel.Children.Add(new TextBlock
        {
            Text = "سيضاف الرصيد إلى الصنف المحدد، وتُسجل الفاتورة وحركة المخزون، ويُحدّث متوسط تكلفة الوحدة بطريقة مرجحة. لا يتم حذف أو استبدال أي رصيد سابق.",
            Foreground = Brush("#F0C9A5"),
            Background = Brush("#211D2A"),
            Padding = new Thickness(10),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12),
            FontSize = 11
        });

        var actions = new WrapPanel();
        var save = ButtonControl("تأكيد استلام الشراء", "#176044", Save_Click);
        var cancel = ButtonControl("إلغاء", "#243B56", (_, _) => this.SetResult(false));
        actions.Children.Add(save);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);

        _status.Text = "أدخل بيانات الاستلام من الفاتورة الفعلية.";
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(_status);
        return panel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_supplier.Text) ||
            !int.TryParse(_quantity.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) ||
            quantity is <= 0 or > 100000 ||
            !decimal.TryParse(_unitCost.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var unitCost) ||
            unitCost is < 0 or > 1_000_000_000m ||
            _supplier.Text.Trim().Length > 120 ||
            _invoice.Text.Trim().Length > 100 ||
            _technician.Text.Trim().Length > 120)
        {
            _status.Text = "تحقق من المورد والكمية وتكلفة الوحدة ومرجع الفاتورة.";
            return;
        }

        Quantity = quantity;
        PurchaseUnitCostEgp = unitCost;
        this.SetResult(true);
    }

    private static void AddField(Panel panel, string label, TextBox box)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush("#9FC7E8"),
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 5)
        });
        box.Height = 35;
        box.Margin = new Thickness(0, 0, 0, 7);
        box.Padding = new Thickness(8, 6, 8, 6);
        box.Background = Brush("#0A1B2D");
        box.Foreground = Brush("#EAF3FF");
        box.BorderBrush = Brush("#35516F");
        panel.Children.Add(box);
    }

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 130,
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
