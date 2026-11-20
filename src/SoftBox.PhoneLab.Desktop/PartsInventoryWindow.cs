using Microsoft.Win32;
using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Extended parts catalogue view for category, supplier, cost/sale pricing, stock thresholds and movement history.
/// </summary>
public sealed class PartsInventoryWindow : Window
{
    private readonly WorkshopStore _store;
    private readonly ObservableCollection<InventoryItem> _items = [];
    private readonly ObservableCollection<MovementDisplay> _movements = [];
    private readonly ListBox _list = new();
    private readonly ListBox _movementList = new();
    private readonly TextBox _sku = new();
    private readonly TextBox _name = new();
    private readonly TextBox _category = new();
    private readonly TextBox _supplier = new();
    private readonly TextBox _quantity = new();
    private readonly TextBox _minimum = new();
    private readonly TextBox _unitCost = new();
    private readonly TextBox _salePrice = new();
    private readonly TextBox _location = new();
    private readonly TextBlock _stockStatus = new();
    private readonly TextBlock _status = new();

    public PartsInventoryWindow(WorkshopStore? store = null)
    {
        _store = store ?? new WorkshopStore();
        Title = "SoftBox | إدارة مخزون قطع الغيار";
        Width = 1120;
        Height = 800;
        MinWidth = 860;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();

        _list.SelectionChanged += (_, _) => LoadSelected();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "مخزون قطع الغيار",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "PARTS CATALOGUE • SUPPLIERS • CATEGORY • COST / SALE • STOCK MOVEMENTS",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(ButtonControl("قطعة جديدة", "#14517A", NewItem_Click));
        actions.Children.Add(ButtonControl("حفظ الصنف", "#176044", SaveItem_Click));
        actions.Children.Add(ButtonControl("حركة مخزون", "#243B56", AdjustStock_Click));
        actions.Children.Add(ButtonControl("تصدير CSV", "#243B56", ExportCsv_Click));
        actions.Children.Add(ButtonControl("تحديث", "#243B56", async (_, _) => await RefreshAsync()));
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.85, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(160) });

        _list.ItemsSource = _items;
        _list.DisplayMemberPath = nameof(InventoryItem.Name);
        _list.Background = Brush("#071625");
        _list.Foreground = Brush("#DCEBFA");
        _list.BorderBrush = Brush("#263D58");
        _list.Padding = new Thickness(8);
        _list.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(_list, 0);
        Grid.SetRow(_list, 0);
        main.Children.Add(_list);

        var formScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var form = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        AddField(form, "SKU / كود القطعة", _sku);
        AddField(form, "اسم القطعة", _name);
        AddField(form, "التصنيف", _category);
        AddField(form, "المورد", _supplier);
        AddField(form, "الكمية الحالية", _quantity);
        AddField(form, "حد إعادة الطلب", _minimum);
        AddField(form, "تكلفة الشراء للوحدة", _unitCost);
        AddField(form, "سعر البيع للوحدة", _salePrice);
        AddField(form, "مكان التخزين", _location);
        _stockStatus.Foreground = Brush("#9FC7E8");
        _stockStatus.Margin = new Thickness(0, 8, 0, 8);
        form.Children.Add(_stockStatus);
        formScroll.Content = form;
        Grid.SetColumn(formScroll, 1);
        Grid.SetRow(formScroll, 0);
        main.Children.Add(formScroll);

        _movementList.ItemsSource = _movements;
        _movementList.DisplayMemberPath = nameof(MovementDisplay.DisplayText);
        _movementList.Background = Brush("#071625");
        _movementList.Foreground = Brush("#DCEBFA");
        _movementList.BorderBrush = Brush("#263D58");
        _movementList.Padding = new Thickness(8);
        Grid.SetColumn(_movementList, 0);
        Grid.SetColumnSpan(_movementList, 2);
        Grid.SetRow(_movementList, 1);
        main.Children.Add(_movementList);

        root.Children.Add(main);
        _status.Text = "جارٍ تحميل المخزون المحلي...";
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private static void AddField(Panel panel, string label, TextBox box)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush("#9FC7E8"),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 5)
        });
        box.Height = 34;
        box.Margin = new Thickness(0, 0, 0, 5);
        box.Padding = new Thickness(8, 6, 8, 6);
        box.Background = Brush("#0A1B2D");
        box.Foreground = Brush("#EAF3FF");
        box.BorderBrush = Brush("#35516F");
        panel.Children.Add(box);
    }

    private async Task RefreshAsync()
    {
        try
        {
            var snapshot = await _store.GetSnapshotAsync();
            var selectedSku = (_list.SelectedItem as InventoryItem)?.Sku;
            _items.Clear();
            foreach (var item in snapshot.Inventory.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
                _items.Add(item);
            _movements.Clear();
            foreach (var movement in snapshot.InventoryMovements.OrderByDescending(item => item.AtUtc).Take(500))
                _movements.Add(new MovementDisplay(movement));
            _list.SelectedItem = _items.FirstOrDefault(item => item.Sku.Equals(selectedSku, StringComparison.OrdinalIgnoreCase));
            _status.Text = $"المخزون: {_items.Count:N0} صنف | الحركات المعروضة: {_movements.Count:N0}.";
            if (_list.SelectedItem is null && _items.Count > 0)
                _list.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تحميل المخزون: " + ex.Message;
        }
    }

    private void NewItem_Click(object sender, RoutedEventArgs e)
    {
        _list.SelectedItem = null;
        _sku.IsReadOnly = false;
        _sku.Clear();
        _name.Clear();
        _category.Clear();
        _supplier.Clear();
        _quantity.Text = "0";
        _minimum.Text = "0";
        _unitCost.Text = "0";
        _salePrice.Text = "0";
        _location.Clear();
        _stockStatus.Text = "أدخل بيانات قطعة جديدة.";
    }

    private void LoadSelected()
    {
        if (_list.SelectedItem is not InventoryItem item)
            return;
        _sku.IsReadOnly = true;
        _sku.Text = item.Sku;
        _name.Text = item.Name;
        _category.Text = item.Category;
        _supplier.Text = item.Supplier;
        _quantity.Text = item.Quantity.ToString(CultureInfo.InvariantCulture);
        _minimum.Text = item.MinimumQuantity.ToString(CultureInfo.InvariantCulture);
        _unitCost.Text = item.UnitCostEgp.ToString(CultureInfo.InvariantCulture);
        _salePrice.Text = item.SalePriceEgp.ToString(CultureInfo.InvariantCulture);
        _location.Text = item.Location;
        _stockStatus.Text = item.NeedsRestock
            ? "تنبيه: الكمية عند حد إعادة الطلب أو أقل."
            : "المخزون فوق حد إعادة الطلب.";
    }

    private async void SaveItem_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(_quantity.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) ||
            !int.TryParse(_minimum.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minimum) ||
            !decimal.TryParse(_unitCost.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var cost) ||
            !decimal.TryParse(_salePrice.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var sale) ||
            quantity < 0 || minimum < 0 || cost < 0 || sale < 0)
        {
            _status.Text = "تحقق من الكمية وحد إعادة الطلب والتكلفة وسعر البيع؛ يجب أن تكون أرقامًا غير سالبة.";
            return;
        }

        try
        {
            await _store.UpsertInventoryItemAsync(new InventoryItem
            {
                Sku = _sku.Text.Trim(),
                Name = _name.Text.Trim(),
                Category = _category.Text.Trim(),
                Supplier = _supplier.Text.Trim(),
                Quantity = quantity,
                MinimumQuantity = minimum,
                UnitCostEgp = cost,
                SalePriceEgp = sale,
                Location = _location.Text.Trim()
            });
            await RefreshAsync();
            _list.SelectedItem = _items.FirstOrDefault(item => item.Sku.Equals(_sku.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            _status.Text = "تم حفظ بيانات الصنف. أي تغيير في الكمية يسجل كحركة مخزون.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر حفظ الصنف: " + ex.Message;
            MessageBox.Show(this, ex.Message, "المخزون", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void AdjustStock_Click(object sender, RoutedEventArgs e)
    {
        if (_list.SelectedItem is not InventoryItem item)
        {
            _status.Text = "اختر صنفًا من المخزون أولًا.";
            return;
        }

        var prompt = new StockMovementPromptWindow(item) { Owner = this };
        if (prompt.ShowDialog() != true)
            return;

        try
        {
            await _store.AdjustStockAsync(item.Sku, prompt.Delta, prompt.Reason);
            await RefreshAsync();
            _list.SelectedItem = _items.FirstOrDefault(candidate => candidate.Sku == item.Sku);
            _status.Text = "تم تسجيل حركة المخزون وتحديث الرصيد.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تسجيل الحركة: " + ex.Message;
            MessageBox.Show(this, ex.Message, "حركة مخزون", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var snapshot = await _store.GetSnapshotAsync();
            var rows = new List<string>
            {
                Csv("SKU", "Name", "Category", "Supplier", "Quantity", "MinimumQuantity", "UnitCost", "SalePrice", "Location")
            };
            rows.AddRange(snapshot.Inventory.Select(item => Csv(
                item.Sku, item.Name, item.Category, item.Supplier,
                item.Quantity.ToString(CultureInfo.InvariantCulture),
                item.MinimumQuantity.ToString(CultureInfo.InvariantCulture),
                item.UnitCostEgp.ToString(CultureInfo.InvariantCulture),
                item.SalePriceEgp.ToString(CultureInfo.InvariantCulture),
                item.Location)));
            var dialog = new SaveFileDialog
            {
                Title = "تصدير سجل المخزون",
                Filter = "CSV (*.csv)|*.csv",
                FileName = "SoftBox-Inventory.csv"
            };
            if (dialog.ShowDialog(this) != true)
                return;
            await File.WriteAllTextAsync(dialog.FileName, string.Join(Environment.NewLine, rows), new UTF8Encoding(true));
            _status.Text = "تم تصدير CSV: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            _status.Text = "فشل تصدير المخزون: " + ex.Message;
        }
    }

    private static string Csv(params string[] fields)
        => string.Join(",", fields.Select(value => "\"" + (value ?? "").Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 105,
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

    private sealed record MovementDisplay(InventoryMovement Movement)
    {
        public string DisplayText => $"{Movement.AtUtc.LocalDateTime:yyyy-MM-dd HH:mm} | {Movement.Sku} | {Movement.ItemName} | {Movement.Delta:+#;-#;0} | رصيد: {Movement.BalanceAfter} | {Movement.Reason}";
    }

    private sealed class StockMovementPromptWindow : Window
    {
        private readonly TextBox _delta = new();
        private readonly TextBox _reason = new();
        public int Delta { get; private set; }
        public string Reason { get; private set; } = "";

        public StockMovementPromptWindow(InventoryItem item)
        {
            Title = "تسجيل حركة مخزون";
            Width = 500;
            Height = 350;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FlowDirection = FlowDirection.RightToLeft;
            Background = Brush("#081321");
            Foreground = Brush("#EAF3FF");

            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock { Text = $"{item.Name} ({item.Sku}) — الرصيد الحالي: {item.Quantity}", Foreground = PartsInventoryWindow.Brush("#B7C9DE"), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "تغيير الكمية (+ إضافة / - صرف)", Margin = new Thickness(0, 12, 0, 5), Foreground = Brush("#9FC7E8") });
            PartsInventoryWindow.Configure(_delta); _delta.Text = "1"; panel.Children.Add(_delta);
            panel.Children.Add(new TextBlock { Text = "سبب الحركة", Margin = new Thickness(0, 12, 0, 5), Foreground = Brush("#9FC7E8") });
            PartsInventoryWindow.Configure(_reason); _reason.Text = "تعديل رصيد المخزون"; panel.Children.Add(_reason);
            var save = PartsInventoryWindow.ButtonControl("تأكيد الحركة", "#176044", Save_Click);
            panel.Children.Add(save);
            Content = panel;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(_delta.Text, out var delta) || delta == 0 || string.IsNullOrWhiteSpace(_reason.Text))
            {
                MessageBox.Show(this, "أدخل كمية تغيير غير صفرية وسببًا واضحًا.", "حركة مخزون", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Delta = delta;
            Reason = _reason.Text.Trim();
            DialogResult = true;
        }

        private static void Configure(TextBox box)
        {
            box.Height = 34; box.Margin = new Thickness(0, 0, 0, 6); box.Padding = new Thickness(8);
            box.Background = Brush("#0A1B2D"); box.Foreground = Brush("#EAF3FF"); box.BorderBrush = Brush("#35516F");
        }
    }
}
