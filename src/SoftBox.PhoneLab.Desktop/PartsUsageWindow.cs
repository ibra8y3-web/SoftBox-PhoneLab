using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Records consumption of an existing inventory part against one repair order.
/// The store performs stock deduction, order usage and movement logging in one atomic write.
/// </summary>
public sealed class PartsUsageWindow : Window
{
    private readonly WorkshopStore _store;
    private readonly RepairOrder _order;
    private readonly Func<Task> _onCompleted;
    private readonly ComboBox _part = new();
    private readonly TextBox _quantity = new();
    private readonly TextBox _technician = new();
    private readonly TextBox _reason = new();
    private readonly TextBlock _stockInfo = new();
    private readonly TextBlock _status = new();
    private readonly Button _consumeButton;
    private List<InventoryItem> _inventory = [];

    public PartsUsageWindow(WorkshopStore store, RepairOrder order, Func<Task> onCompleted)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _order = order ?? throw new ArgumentNullException(nameof(order));
        _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));

        Title = $"SoftBox | صرف قطعة لأمر الصيانة {_order.TicketNumber}";
        Width = 680;
        Height = 560;
        MinWidth = 560;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _consumeButton = ButtonControl("تأكيد صرف القطعة", "#176044", Consume_Click);
        Content = BuildContent();
        _part.SelectionChanged += (_, _) => UpdateStockInfo();
        Loaded += async (_, _) => await LoadInventoryAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "صرف قطعة غيار لأمر الصيانة",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = $"أمر الصيانة: {_order.TicketNumber} | {_order.CustomerName} | {_order.DeviceModel}",
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var form = new StackPanel();
        AddLabel(form, "قطعة الغيار الموجودة في المخزون");
        _part.Height = 36;
        _part.DisplayMemberPath = nameof(InventoryChoice.Label);
        _part.Margin = new Thickness(0, 0, 0, 8);
        _part.Background = Brush("#0A1B2D");
        _part.Foreground = Brush("#EAF3FF");
        _part.BorderBrush = Brush("#35516F");
        form.Children.Add(_part);

        _stockInfo.Foreground = Brush("#9FC7E8");
        _stockInfo.TextWrapping = TextWrapping.Wrap;
        _stockInfo.Margin = new Thickness(0, 0, 0, 10);
        form.Children.Add(_stockInfo);

        AddLabel(form, "الكمية المستخدمة");
        ConfigureInput(_quantity);
        _quantity.Text = "1";
        form.Children.Add(_quantity);

        AddLabel(form, "الفني المسؤول");
        ConfigureInput(_technician);
        _technician.Text = Environment.UserName;
        form.Children.Add(_technician);

        AddLabel(form, "سبب الصرف / ملاحظة");
        ConfigureInput(_reason);
        _reason.Text = "استخدام قطعة في أمر الصيانة";
        form.Children.Add(_reason);

        var warning = new Border
        {
            Background = Brush("#211D2A"),
            BorderBrush = Brush("#66516B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 12),
            Child = new TextBlock
            {
                Text = "سيتم خصم الكمية من المخزون وربطها بأمر الصيانة وتسجيل حركة صرف وتكلفة القطع. لا يمكن للصرف تجاوز الرصيد المتاح.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        form.Children.Add(warning);
        form.Children.Add(_consumeButton);

        _status.Text = "اختر قطعة وحدد الكمية.";
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 10, 0, 0);
        form.Children.Add(_status);

        root.Children.Add(form);
        return root;
    }

    private async Task LoadInventoryAsync()
    {
        try
        {
            var snapshot = await _store.GetSnapshotAsync();
            _inventory = snapshot.Inventory
                .Where(item => item.Quantity > 0)
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _part.ItemsSource = _inventory.Select(item => new InventoryChoice(item, $"{item.Name} | {item.Sku} | المتاح: {item.Quantity}")).ToArray();
            if (_part.Items.Count > 0)
                _part.SelectedIndex = 0;
            else
            {
                _stockInfo.Text = "لا توجد قطع متاحة في المخزون. أضف قطعة أو استقبل كمية قبل الصرف.";
                _consumeButton.IsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر قراءة المخزون: " + ex.Message;
            _consumeButton.IsEnabled = false;
        }
    }

    private void UpdateStockInfo()
    {
        if (_part.SelectedItem is not InventoryChoice choice)
            return;
        _stockInfo.Text = $"الرصيد المتاح: {choice.Item.Quantity} | تكلفة الوحدة: {choice.Item.UnitCostEgp:0.00} | سعر البيع المسجل: {choice.Item.SalePriceEgp:0.00}";
    }

    private async void Consume_Click(object sender, RoutedEventArgs e)
    {
        if (_part.SelectedItem is not InventoryChoice selected)
        {
            _status.Text = "اختر قطعة من المخزون أولًا.";
            return;
        }
        if (!int.TryParse(_quantity.Text, out var quantity) || quantity <= 0)
        {
            _status.Text = "أدخل كمية صحيحة أكبر من صفر.";
            return;
        }
        if (quantity > selected.Item.Quantity)
        {
            _status.Text = $"الرصيد غير كافٍ. المتاح: {selected.Item.Quantity}.";
            return;
        }

        var estimatedCost = quantity * selected.Item.UnitCostEgp;
        var confirmation = MessageBox.Show(this,
            $"أمر الصيانة: {_order.TicketNumber}\nالقطعة: {selected.Item.Name}\nالكمية: {quantity}\nتكلفة القطع: {estimatedCost:0.00}\nالرصيد بعد الصرف: {selected.Item.Quantity - quantity}\n\nتأكيد الصرف؟",
            "تأكيد حركة المخزون", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        _consumeButton.IsEnabled = false;
        try
        {
            var usage = await _store.ConsumeInventoryPartForOrderAsync(
                _order.Id,
                selected.Item.Sku,
                quantity,
                _technician.Text,
                _reason.Text);
            _status.Text = $"تم صرف {usage.Quantity} × {usage.PartName}. تكلفة القطع المسجلة: {usage.TotalCostEgp:0.00}.";
            await _onCompleted();
            await LoadInventoryAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "لم يتم صرف القطعة: " + ex.Message;
            MessageBox.Show(this, ex.Message, "حركة المخزون",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _consumeButton.IsEnabled = true;
        }
    }

    private static void AddLabel(Panel panel, string label)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = Brush("#9FC7E8"),
            Margin = new Thickness(0, 3, 0, 5)
        });
    }

    private static void ConfigureInput(TextBox box)
    {
        box.Height = 34;
        box.Margin = new Thickness(0, 0, 0, 8);
        box.Padding = new Thickness(8, 6, 8, 6);
        box.Background = Brush("#0A1B2D");
        box.Foreground = Brush("#EAF3FF");
        box.BorderBrush = Brush("#35516F");
    }

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            MinWidth = 160,
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

    private sealed record InventoryChoice(InventoryItem Item, string Label);
}
