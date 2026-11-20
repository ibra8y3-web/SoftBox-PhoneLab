using Microsoft.Win32;
using System.IO;
using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using QRCoder;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>Offline repair-shop workspace for customers, repair orders, inventory and backups.</summary>
public sealed class WorkshopCenterWindow : Window
{
    private readonly WorkshopStore _store = new();
    private readonly DataGrid _ordersGrid = GridView();
    private readonly DataGrid _customersGrid = GridView();
    private readonly DataGrid _inventoryGrid = GridView();
    private readonly DataGrid _eventsGrid = GridView();
    private readonly TextBlock _status = new() { Foreground = Brushes.LightSkyBlue, Margin = new Thickness(8) };
    private readonly TextBox _customerName = Input(210);
    private readonly TextBox _customerEditName = Input(210);
    private readonly TextBox _customerEditPhone = Input(155);
    private readonly TextBox _customerEditNotes = Input(300, true);
    private readonly TextBox _customerPhone = Input(155);
    private readonly TextBox _deviceModel = Input(190);
    private readonly TextBox _serialNote = Input(175);
    private readonly TextBox _symptoms = Input(320);
    private readonly TextBox _before = Input(320, true);
    private readonly TextBox _workDone = Input(320, true);
    private readonly TextBox _after = Input(320, true);
    private readonly TextBox _estimate = Input(100);
    private readonly TextBox _finalCost = Input(100);
    private readonly ComboBox _orderStatus = new() { Width = 150, Margin = new Thickness(5), ItemsSource = new[] { "تم الاستلام", "قيد التشخيص", "بانتظار قطع غيار", "قيد الإصلاح", "جاهز للتسليم", "تم التسليم", "ملغي" }, SelectedIndex = 0 };
    private readonly TextBox _sku = Input(120);
    private readonly TextBox _partName = Input(210);
    private readonly TextBox _quantity = Input(80);
    private readonly TextBox _minimum = Input(80);
    private readonly TextBox _unitCost = Input(100);
    private readonly TextBox _location = Input(150);
    private readonly TextBox _stockDelta = Input(80);
    private readonly TextBox _stockReason = Input(210);
    private RepairOrder? _selectedOrder;
    private WorkshopData _snapshot = new();

    public WorkshopCenterWindow()
    {
        Title = "SoftBox PhoneLab Professional | مركز الصيانة";
        Width = 1500;
        Height = 940;
        MinWidth = 1100;
        MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildLayout();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = "مركز إدارة الصيانة", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Brush("#7DD3FC") });
        header.Children.Add(new TextBlock { Text = "العملاء • أوامر الصيانة • المخزون • سجل العمليات • النسخ الاحتياطي المحلي", Margin = new Thickness(0, 5, 0, 7), Foreground = Brush("#9CB3CC") });
        header.Children.Add(_status);
        header.Children.Add(Button("Global Workshop | الإدارة الدولية", (_, _) =>
        {
            var window = new GlobalWorkshopWindow { Owner = this };
            window.Show();
        }));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "أوامر الصيانة", Content = BuildOrdersTab() });
        tabs.Items.Add(new TabItem { Header = "العملاء", Content = BuildCustomersTab() });
        tabs.Items.Add(new TabItem { Header = "قطع الغيار والمخزون", Content = BuildInventoryTab() });
        tabs.Items.Add(new TabItem { Header = "سجل العمليات", Content = BuildEventsTab() });
        tabs.Items.Add(new TabItem { Header = "النسخ الاحتياطي", Content = BuildBackupTab() });
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildOrdersTab()
    {
        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var form = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        form.Children.Add(Field("اسم العميل *", _customerName));
        form.Children.Add(Field("رقم الهاتف", _customerPhone));
        form.Children.Add(Field("موديل الجهاز *", _deviceModel));
        form.Children.Add(Field("ملاحظة سيريال/IMEI", _serialNote));
        form.Children.Add(Field("العطل المبلغ عنه *", _symptoms));
        form.Children.Add(Field("التكلفة التقديرية (جنيه)", _estimate));
        var add = Button("إنشاء أمر صيانة", async (_, _) => await CreateOrderAsync());
        form.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { add } });
        Grid.SetRow(form, 1);
        root.Children.Add(form);

        ConfigureOrdersGrid();
        _ordersGrid.SelectionChanged += (_, _) => SelectOrder();
        Grid.SetRow(_ordersGrid, 2);
        root.Children.Add(_ordersGrid);

        var editor = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        editor.Children.Add(new TextBlock { Text = "تفاصيل ما قبل الإصلاح وما بعده — أدخل الملاحظات والنتائج الفعلية فقط", Foreground = Brush("#A9C1DA"), FontWeight = FontWeights.SemiBold });
        var editFields = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
        editFields.Children.Add(Field("نتائج الفحص قبل الإصلاح", _before));
        editFields.Children.Add(Field("الأعمال المنفذة", _workDone));
        editFields.Children.Add(Field("نتائج الاختبار بعد الإصلاح", _after));
        editFields.Children.Add(Field("الحالة", _orderStatus));
        editFields.Children.Add(Field("التكلفة النهائية", _finalCost));
        editor.Children.Add(editFields);
        var actions = new WrapPanel();
        actions.Children.Add(Button("حفظ تحديث الأمر", async (_, _) => await UpdateOrderAsync()));
        actions.Children.Add(Button("تصدير تقرير الأمر", async (_, _) => await ExportOrderAsync()));
        actions.Children.Add(Button("إنشاء QR للعملية", async (_, _) => await ExportQrAsync()));
        editor.Children.Add(actions);
        Grid.SetRow(editor, 3);
        root.Children.Add(editor);
        return root;
    }

    private UIElement BuildCustomersTab()
    {
        var root = new DockPanel { Margin = new Thickness(8) };
        var intro = new TextBlock
        {
            Text = "قائمة العملاء تُنشأ تلقائيًا عند تسجيل أمر صيانة جديد. رقم الهاتف وسيلة المطابقة الاختيارية؛ البيانات محفوظة محليًا على هذا الكمبيوتر.",
            Foreground = Brush("#A9C1DA"), Margin = new Thickness(5, 5, 5, 12), TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);
        var edit = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        edit.Children.Add(Field("اسم العميل", _customerEditName));
        edit.Children.Add(Field("الهاتف", _customerEditPhone));
        edit.Children.Add(Field("ملاحظات", _customerEditNotes));
        edit.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { Button("حفظ / تحديث العميل", async (_, _) => await SaveCustomerAsync()) } });
        DockPanel.SetDock(edit, Dock.Top);
        root.Children.Add(edit);
        _customersGrid.Columns.Add(Column("الاسم", nameof(CustomerRecord.Name), 2));
        _customersGrid.Columns.Add(Column("الهاتف", nameof(CustomerRecord.Phone), 1.2));
        _customersGrid.Columns.Add(Column("ملاحظات", nameof(CustomerRecord.Notes), 2));
        _customersGrid.Columns.Add(Column("تاريخ التسجيل UTC", nameof(CustomerRecord.CreatedAtUtc), 1.5));
        _customersGrid.SelectionChanged += (_, _) =>
        {
            if (_customersGrid.SelectedItem is CustomerRecord customer)
            {
                _customerEditName.Text = customer.Name;
                _customerEditPhone.Text = customer.Phone;
                _customerEditNotes.Text = customer.Notes;
            }
        };
        root.Children.Add(_customersGrid);
        return root;
    }

    private UIElement BuildInventoryTab()
    {
        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var addFields = new WrapPanel { Margin = new Thickness(0, 5, 0, 10) };
        addFields.Children.Add(Field("كود الصنف (SKU) *", _sku));
        addFields.Children.Add(Field("اسم القطعة *", _partName));
        addFields.Children.Add(Field("الرصيد", _quantity));
        addFields.Children.Add(Field("حد إعادة الطلب", _minimum));
        addFields.Children.Add(Field("تكلفة الوحدة (جنيه)", _unitCost));
        addFields.Children.Add(Field("مكان التخزين", _location));
        addFields.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { Button("إضافة / تحديث الصنف", async (_, _) => await SaveInventoryAsync()) } });
        Grid.SetRow(addFields, 0);
        root.Children.Add(addFields);

        var stockActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        stockActions.Children.Add(Field("تغيير الكمية (+ أو -)", _stockDelta));
        stockActions.Children.Add(Field("سبب الحركة", _stockReason));
        stockActions.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { Button("تنفيذ حركة مخزون", async (_, _) => await ChangeStockAsync()) } });
        Grid.SetRow(stockActions, 1);
        root.Children.Add(stockActions);

        _inventoryGrid.Columns.Add(Column("SKU", nameof(InventoryItem.Sku), 1));
        _inventoryGrid.Columns.Add(Column("اسم القطعة", nameof(InventoryItem.Name), 2));
        _inventoryGrid.Columns.Add(Column("الرصيد", nameof(InventoryItem.Quantity), .7));
        _inventoryGrid.Columns.Add(Column("حد التنبيه", nameof(InventoryItem.MinimumQuantity), .8));
        _inventoryGrid.Columns.Add(Column("تكلفة الوحدة", nameof(InventoryItem.UnitCostEgp), 1));
        _inventoryGrid.Columns.Add(Column("مكان التخزين", nameof(InventoryItem.Location), 1));
        _inventoryGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "إعادة طلب", Binding = new Binding(nameof(InventoryItem.NeedsRestock)), IsReadOnly = true });
        _inventoryGrid.SelectionChanged += (_, _) =>
        {
            if (_inventoryGrid.SelectedItem is InventoryItem item)
            {
                _sku.Text = item.Sku;
                _partName.Text = item.Name;
                _quantity.Text = item.Quantity.ToString();
                _minimum.Text = item.MinimumQuantity.ToString();
                _unitCost.Text = item.UnitCostEgp.ToString("0.##");
                _location.Text = item.Location;
            }
        };
        Grid.SetRow(_inventoryGrid, 2);
        root.Children.Add(_inventoryGrid);
        return root;
    }

    private UIElement BuildEventsTab()
    {
        var root = new DockPanel { Margin = new Thickness(8) };
        var note = new TextBlock { Text = "سجل آخر 5000 حركة محفوظة محليًا لمراجعة إنشاء الأوامر وتغيير الحالات وحركات المخزون.", Foreground = Brush("#A9C1DA"), Margin = new Thickness(4, 4, 4, 10) };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);
        _eventsGrid.Columns.Add(Column("التاريخ UTC", nameof(WorkshopEvent.AtUtc), 1.5));
        _eventsGrid.Columns.Add(Column("النوع", nameof(WorkshopEvent.Category), 1));
        _eventsGrid.Columns.Add(Column("المرجع", nameof(WorkshopEvent.Reference), 1.2));
        _eventsGrid.Columns.Add(Column("التفاصيل", nameof(WorkshopEvent.Description), 3));
        root.Children.Add(_eventsGrid);
        return root;
    }

    private UIElement BuildBackupTab()
    {
        var panel = new StackPanel { Margin = new Thickness(18), MaxWidth = 900 };
        panel.Children.Add(new TextBlock { Text = "قاعدة بيانات محلية تعمل دون إنترنت", FontSize = 19, FontWeight = FontWeights.Bold, Foreground = Brush("#7DD3FC") });
        panel.Children.Add(new TextBlock
        {
            Text = "المسار الحالي:\n" + _store.DatabasePath + "\n\nتتضمن النسخة العملاء وأوامر الصيانة والمخزون وسجل الحركات. احتفظ بنسخة على وسيط آمن، ولا تشاركها علنًا لأنها قد تحتوي بيانات تواصل العملاء.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 18), Foreground = Brush("#B7C9DE")
        });
        var actions = new WrapPanel();
        actions.Children.Add(Button("إنشاء نسخة احتياطية", async (_, _) => await BackupAsync()));
        actions.Children.Add(Button("استعادة نسخة احتياطية", async (_, _) => await RestoreAsync()));
        actions.Children.Add(Button("فتح مجلد قاعدة البيانات", (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", Path.GetDirectoryName(_store.DatabasePath)!) { UseShellExecute = true }); }
            catch (Exception ex) { SetStatus("تعذر فتح المجلد: " + ex.Message); }
        }));
        panel.Children.Add(actions);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void ConfigureOrdersGrid()
    {
        _ordersGrid.Columns.Add(Column("رقم العملية", nameof(RepairOrder.TicketNumber), 1.2));
        _ordersGrid.Columns.Add(Column("العميل", nameof(RepairOrder.CustomerName), 1.1));
        _ordersGrid.Columns.Add(Column("الهاتف", nameof(RepairOrder.CustomerPhone), 1));
        _ordersGrid.Columns.Add(Column("الجهاز", nameof(RepairOrder.DeviceModel), 1.2));
        _ordersGrid.Columns.Add(Column("العطل عند الاستلام", nameof(RepairOrder.IntakeSymptoms), 2));
        _ordersGrid.Columns.Add(Column("الحالة", nameof(RepairOrder.Status), 1));
        _ordersGrid.Columns.Add(Column("التقديرية", nameof(RepairOrder.EstimatedCostEgp), .8));
        _ordersGrid.Columns.Add(Column("النهائية", nameof(RepairOrder.FinalCostEgp), .8));
    }

    private async Task SaveCustomerAsync()
    {
        try
        {
            var selected = _customersGrid.SelectedItem as CustomerRecord;
            await _store.UpsertCustomerAsync(new CustomerRecord
            {
                Id = selected?.Id ?? Guid.NewGuid(),
                Name = _customerEditName.Text,
                Phone = _customerEditPhone.Text,
                Notes = _customerEditNotes.Text,
                CreatedAtUtc = selected?.CreatedAtUtc ?? DateTimeOffset.UtcNow
            });
            await RefreshAsync();
            SetStatus("تم حفظ بيانات العميل محليًا.");
        }
        catch (Exception ex) { SetStatus("تعذر حفظ العميل: " + ex.Message); }
    }

    private async Task CreateOrderAsync()
    {
        try
        {
            var estimated = ParseMoney(_estimate.Text);
            var order = new RepairOrder
            {
                CustomerName = _customerName.Text,
                CustomerPhone = _customerPhone.Text,
                DeviceModel = _deviceModel.Text,
                SerialOrImeiNote = _serialNote.Text,
                IntakeSymptoms = _symptoms.Text,
                EstimatedCostEgp = estimated,
                WarrantyMonths = _snapshot.Settings.DefaultWarrantyMonths
            };
            var created = await _store.CreateRepairOrderAsync(order);
            _customerName.Clear(); _customerPhone.Clear(); _deviceModel.Clear(); _serialNote.Clear(); _symptoms.Clear(); _estimate.Clear();
            await RefreshAsync();
            _ordersGrid.SelectedItem = _snapshot.RepairOrders.FirstOrDefault(x => x.Id == created.Id);
            SetStatus("تم إنشاء أمر الصيانة " + created.TicketNumber + " وحفظه محليًا.");
        }
        catch (Exception ex) { SetStatus("تعذر إنشاء الأمر: " + ex.Message); }
    }

    private void SelectOrder()
    {
        _selectedOrder = _ordersGrid.SelectedItem as RepairOrder;
        if (_selectedOrder is null) return;
        _before.Text = _selectedOrder.FindingsBeforeRepair;
        _workDone.Text = _selectedOrder.WorkPerformed;
        _after.Text = _selectedOrder.ResultsAfterRepair;
        _orderStatus.SelectedItem = _selectedOrder.Status;
        _finalCost.Text = _selectedOrder.FinalCostEgp.ToString("0.##");
    }

    private async Task UpdateOrderAsync()
    {
        if (_selectedOrder is null) { SetStatus("اختر أمر صيانة أولًا."); return; }
        try
        {
            _selectedOrder.FindingsBeforeRepair = _before.Text;
            _selectedOrder.WorkPerformed = _workDone.Text;
            _selectedOrder.ResultsAfterRepair = _after.Text;
            _selectedOrder.Status = _orderStatus.SelectedItem as string ?? "قيد التشخيص";
            _selectedOrder.FinalCostEgp = ParseMoney(_finalCost.Text);
            await _store.UpdateRepairOrderAsync(_selectedOrder);
            await RefreshAsync();
            _ordersGrid.SelectedItem = _snapshot.RepairOrders.FirstOrDefault(x => x.Id == _selectedOrder.Id);
            SetStatus("تم حفظ تحديثات " + _selectedOrder.TicketNumber + ".");
        }
        catch (Exception ex) { SetStatus("تعذر تحديث الأمر: " + ex.Message); }
    }

    private async Task ExportOrderAsync()
    {
        if (_selectedOrder is null) { SetStatus("اختر أمر صيانة لتصديره."); return; }
        var dialog = new SaveFileDialog { Title = "تصدير تقرير أمر الصيانة", Filter = "Text report|*.txt", FileName = _selectedOrder.TicketNumber + ".txt" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await _store.ExportOrderAsync(_selectedOrder.TicketNumber, dialog.FileName);
            SetStatus("تم تصدير تقرير " + _selectedOrder.TicketNumber + ".");
        }
        catch (Exception ex) { SetStatus("تعذر التصدير: " + ex.Message); }
    }

    private async Task ExportQrAsync()
    {
        if (_selectedOrder is null) { SetStatus("اختر أمر صيانة لإنشاء QR."); return; }
        var dialog = new SaveFileDialog
        {
            Title = "حفظ رمز QR لرقم عملية الصيانة",
            Filter = "PNG image|*.png",
            FileName = _selectedOrder.TicketNumber + "-QR.png"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var payload = "softbox-phonelab://repair/" + Uri.EscapeDataString(_selectedOrder.TicketNumber);
            using var generator = new QRCodeGenerator();
            using var qrData = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(qrData);
            var bytes = png.GetGraphic(12);
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            SetStatus("تم إنشاء QR للعملية " + _selectedOrder.TicketNumber + ". الرمز يحتوي رقم العملية فقط ولا يتضمن بيانات العميل.");
        }
        catch (Exception ex) { SetStatus("تعذر إنشاء QR: " + ex.Message); }
    }

    private async Task SaveInventoryAsync()
    {
        try
        {
            await _store.UpsertInventoryItemAsync(new InventoryItem
            {
                Sku = _sku.Text,
                Name = _partName.Text,
                Quantity = ParseInt(_quantity.Text),
                MinimumQuantity = ParseInt(_minimum.Text),
                UnitCostEgp = ParseMoney(_unitCost.Text),
                Location = _location.Text
            });
            await RefreshAsync();
            SetStatus("تم حفظ بيانات الصنف.");
        }
        catch (Exception ex) { SetStatus("تعذر حفظ الصنف: " + ex.Message); }
    }

    private async Task ChangeStockAsync()
    {
        try
        {
            var delta = ParseInt(_stockDelta.Text);
            await _store.AdjustStockAsync(_sku.Text, delta, string.IsNullOrWhiteSpace(_stockReason.Text) ? "تعديل يدوي بواسطة الفني" : _stockReason.Text);
            await RefreshAsync();
            SetStatus("تم تسجيل حركة المخزون.");
        }
        catch (Exception ex) { SetStatus("تعذر تنفيذ الحركة: " + ex.Message); }
    }

    private async Task BackupAsync()
    {
        var dialog = new SaveFileDialog { Title = "حفظ نسخة احتياطية لبيانات PhoneLab", Filter = "JSON backup|*.json", FileName = $"PhoneLab-Workshop-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { var path = await _store.CreateBackupAsync(dialog.FileName); SetStatus("تم إنشاء النسخة الاحتياطية: " + path); }
        catch (Exception ex) { SetStatus("فشل النسخ الاحتياطي: " + ex.Message); }
    }

    private async Task RestoreAsync()
    {
        var dialog = new OpenFileDialog { Title = "اختر نسخة PhoneLab الاحتياطية", Filter = "JSON backup|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "سيتم استبدال قاعدة بيانات مركز الصيانة الحالية بمحتويات النسخة المختارة. أنشئ نسخة احتياطية حالية أولًا عند الحاجة.\n\nهل تريد المتابعة؟",
            "تأكيد استعادة البيانات", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await _store.RestoreBackupAsync(dialog.FileName); await RefreshAsync(); SetStatus("تمت استعادة قاعدة البيانات المحلية."); }
        catch (Exception ex) { SetStatus("فشلت الاستعادة: " + ex.Message); }
    }

    private async Task RefreshAsync()
    {
        _snapshot = await _store.GetSnapshotAsync();
        _ordersGrid.ItemsSource = _snapshot.RepairOrders.OrderByDescending(x => x.ReceivedAtUtc).ToList();
        _customersGrid.ItemsSource = _snapshot.Customers.OrderBy(x => x.Name).ToList();
        _inventoryGrid.ItemsSource = _snapshot.Inventory.OrderBy(x => x.Name).ToList();
        _eventsGrid.ItemsSource = _snapshot.Events.OrderByDescending(x => x.AtUtc).ToList();
        SetStatus($"العملاء: {_snapshot.Customers.Count} | أوامر الصيانة: {_snapshot.RepairOrders.Count} | أصناف المخزون: {_snapshot.Inventory.Count} | السجل: {_snapshot.Events.Count}");
    }

    private void SetStatus(string message) => _status.Text = message;

    private static decimal ParseMoney(string text)
        => string.IsNullOrWhiteSpace(text) ? 0 : decimal.Parse(text, System.Globalization.CultureInfo.CurrentCulture);

    private static int ParseInt(string text)
        => string.IsNullOrWhiteSpace(text) ? 0 : int.Parse(text, System.Globalization.CultureInfo.CurrentCulture);

    private static DataGrid GridView() => new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        IsReadOnly = true,
        SelectionMode = DataGridSelectionMode.Single,
        SelectionUnit = DataGridSelectionUnit.FullRow,
        Background = Brush("#0D1C2E"),
        Foreground = Brush("#EAF3FF"),
        RowBackground = Brush("#12243A"),
        AlternatingRowBackground = Brush("#172B43"),
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        Margin = new Thickness(0, 5, 0, 0)
    };

    private static DataGridTextColumn Column(string header, string property, double weight)
        => new() { Header = header, Binding = new Binding(property), Width = new DataGridLength(weight, DataGridLengthUnitType.Star), IsReadOnly = true };

    private static TextBox Input(double width, bool multiLine = false) => new()
    {
        Width = width,
        MinHeight = multiLine ? 62 : 34,
        Height = multiLine ? 62 : 34,
        AcceptsReturn = multiLine,
        TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalContentAlignment = VerticalAlignment.Center,
        Background = Brush("#0B192A"),
        Foreground = Brush("#F1F7FF"),
        BorderBrush = Brush("#36516F"),
        Padding = new Thickness(8, 5, 8, 5),
        Margin = new Thickness(4)
    };

    private static UIElement Field(string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(3, 2, 3, 2) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("#9FB5CC"), FontSize = 11, Margin = new Thickness(5, 0, 0, 2) });
        panel.Children.Add(control);
        return panel;
    }

    private static Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text, Padding = new Thickness(13, 8, 13, 8), Margin = new Thickness(4),
            Background = Brush("#14517A"), Foreground = Brushes.White, BorderBrush = Brush("#2F83B5"),
            FontWeight = FontWeights.SemiBold, MinHeight = 34, Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += click;
        return button;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
