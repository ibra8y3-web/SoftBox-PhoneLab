using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Models;
using SoftBox.PhoneLab.Desktop.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>International workshop administration: settings, searchable orders, warranty, timeline and exports.</summary>
public sealed class GlobalWorkshopWindow : Window
{
    private readonly WorkshopStore _store = new();
    private readonly int _initialTabIndex;
    private WorkshopData _data = new();
    private RepairOrder? _selectedOrder;

    private readonly TextBlock _dashboard = new() { Margin = new Thickness(6), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush("#7DD3FC") };
    private readonly DataGrid _orders = GridView();
    private readonly DataGrid _timeline = GridView();
    private readonly DataGrid _partsUsed = GridView();
    private readonly DataGrid _payments = GridView();
    private readonly TextBlock _partsCost = new() { Foreground = Brush("#9FC7E8"), Margin = new Thickness(6, 4, 6, 4) };
    private readonly TextBlock _financeSummary = new() { Foreground = Brush("#B7DDF7"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5, 2, 5, 4) };
    private readonly TextBlock _analyticsSummary = new() { Foreground = Brush("#B7DDF7"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 10, 6, 10) };
    private readonly DataGrid _analyticsFaults = GridView();
    private readonly DataGrid _analyticsModels = GridView();
    private readonly DataGrid _analyticsTechnicians = GridView();
    private readonly DataGrid _analyticsMonthly = GridView();
    private readonly TextBox _search = Box(250);
    private readonly ComboBox _statusFilter = new() { Width = 170, Margin = new Thickness(5), ItemsSource = StatusesWithAll(), SelectedIndex = 0 };
    private readonly ComboBox _status = new() { Width = 175, Margin = new Thickness(5), ItemsSource = Statuses(), SelectedIndex = 1 };
    private readonly TextBox _technician = Box(180);
    private readonly TextBox _warrantyMonths = Box(80);
    private readonly TextBox _before = Box(380, true);
    private readonly TextBox _work = Box(380, true);
    private readonly TextBox _after = Box(380, true);
    private readonly TextBox _finalCost = Box(110);
    private readonly TextBlock _result = new() { Margin = new Thickness(8), Foreground = Brush("#A9C1DA"), TextWrapping = TextWrapping.Wrap };

    private readonly TextBox _businessName = Box(230);
    private readonly TextBox _country = Box(170);
    private readonly TextBox _address = Box(330);
    private readonly TextBox _phone = Box(180);
    private readonly TextBox _email = Box(230);
    private readonly TextBox _tax = Box(180);
    private readonly TextBox _currency = Box(90);
    private readonly TextBox _culture = Box(140);
    private readonly TextBox _timeZone = Box(260);
    private readonly TextBox _defaultWarranty = Box(90);
    private readonly ComboBox _reportLanguage = new() { Width = 110, Margin = new Thickness(5), ItemsSource = new[] { "ar", "en" }, SelectedIndex = 0 };

    public GlobalWorkshopWindow(int initialTabIndex = 0)
    {
        _initialTabIndex = Math.Clamp(initialTabIndex, 0, 2);
        Title = "SoftBox PhoneLab Global | Workshop Operations";
        Width = 1500;
        Height = 950;
        MinWidth = 1100;
        MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildUi();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private UIElement BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock { Text = "SoftBox PhoneLab Global", FontSize = 25, FontWeight = FontWeights.Bold, Foreground = Brush("#7DD3FC") });
        header.Children.Add(new TextBlock { Text = "إدارة عالمية للمركز • بحث • ضمان • سجل فني • عملات • تقارير", Foreground = Brush("#A9C1DA"), Margin = new Thickness(0, 4, 0, 2) });
        header.Children.Add(_dashboard);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        DockPanel.SetDock(_result, Dock.Bottom);
        root.Children.Add(_result);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "العمليات والضمان", Content = BuildOrdersPage() });
        tabs.Items.Add(new TabItem { Header = "بيانات المركز الدولية", Content = BuildSettingsPage() });
        tabs.Items.Add(new TabItem { Header = "تصدير وتقارير", Content = BuildExportsPage() });
        tabs.SelectedIndex = _initialTabIndex;
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildOrdersPage()
    {
        var page = new Grid { Margin = new Thickness(8) };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(300) });

        var filters = new WrapPanel();
        filters.Children.Add(Field("بحث: رقم العملية / العميل / الهاتف / الجهاز", _search));
        filters.Children.Add(Field("الحالة", _statusFilter));
        filters.Children.Add(Button("تحديث", async (_, _) => await RefreshAsync()));
        _search.TextChanged += (_, _) => ApplyFilter();
        _statusFilter.SelectionChanged += (_, _) => ApplyFilter();
        Grid.SetRow(filters, 0);
        page.Children.Add(filters);

        _orders.Columns.Add(Column("Ticket", nameof(RepairOrder.TicketNumber), 1.1));
        _orders.Columns.Add(Column("العميل", nameof(RepairOrder.CustomerName), 1));
        _orders.Columns.Add(Column("الهاتف", nameof(RepairOrder.CustomerPhone), 1));
        _orders.Columns.Add(Column("الجهاز", nameof(RepairOrder.DeviceModel), 1));
        _orders.Columns.Add(Column("العطل", nameof(RepairOrder.IntakeSymptoms), 1.5));
        _orders.Columns.Add(Column("الحالة", nameof(RepairOrder.Status), .9));
        _orders.Columns.Add(Column("الفني", nameof(RepairOrder.TechnicianName), .9));
        _orders.Columns.Add(Column("الضمان شهر", nameof(RepairOrder.WarrantyMonths), .7));
        _orders.Columns.Add(Column("ينتهي UTC", nameof(RepairOrder.WarrantyEndsAtUtc), 1.1));
        _orders.SelectionChanged += (_, _) => SelectOrder();
        Grid.SetRow(_orders, 1);
        page.Children.Add(_orders);

        var editor = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(155) });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddEditorField(editor, "نتائج الفحص قبل الإصلاح", _before, 0);
        AddEditorField(editor, "الأعمال المنفذة", _work, 1);
        AddEditorField(editor, "النتائج بعد الإصلاح", _after, 2);
        var side = new StackPanel();
        side.Children.Add(Field("الحالة", _status));
        side.Children.Add(Field("الفني المسؤول", _technician));
        side.Children.Add(Field("مدة الضمان بالشهور", _warrantyMonths));
        side.Children.Add(Field("التكلفة النهائية بعملة المركز", _finalCost));
        side.Children.Add(_financeSummary);
        Grid.SetColumn(side, 3);
        Grid.SetRowSpan(side, 2);
        editor.Children.Add(side);
        _timeline.Columns.Add(Column("UTC", nameof(RepairOrderNote.AtUtc), 1.1));
        _timeline.Columns.Add(Column("الفني", nameof(RepairOrderNote.TechnicianName), 1));
        _timeline.Columns.Add(Column("الملاحظة", nameof(RepairOrderNote.Note), 3));
        Grid.SetColumn(_timeline, 0);
        Grid.SetColumnSpan(_timeline, 3);
        Grid.SetRow(_timeline, 1);
        editor.Children.Add(_timeline);

        _partsUsed.Columns.Add(Column("كود الصنف", nameof(RepairOrderPartUsage.Sku), .9));
        _partsUsed.Columns.Add(Column("القطعة", nameof(RepairOrderPartUsage.PartName), 1.4));
        _partsUsed.Columns.Add(Column("الكمية", nameof(RepairOrderPartUsage.Quantity), .5));
        _partsUsed.Columns.Add(Column("تكلفة الوحدة", nameof(RepairOrderPartUsage.UnitCostEgp), .7));
        _partsUsed.Columns.Add(Column("الإجمالي", nameof(RepairOrderPartUsage.TotalCostEgp), .7));
        _payments.Columns.Add(Column("تاريخ التسجيل", nameof(RepairPaymentRecord.ReceivedAtUtc), 1));
        _payments.Columns.Add(Column("المبلغ", nameof(RepairPaymentRecord.AmountEgp), .6));
        _payments.Columns.Add(Column("الطريقة", nameof(RepairPaymentRecord.Method), .7));
        _payments.Columns.Add(Column("المرجع", nameof(RepairPaymentRecord.Reference), .8));
        _payments.Columns.Add(Column("المتبقي بعد الدفعة", nameof(RepairPaymentRecord.BalanceAfterPaymentEgp), .8));
        var financePanel = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        financePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        financePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        financePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        financePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var partsLabel = new TextBlock { Text = "قطع الغيار المصروفة", Foreground = Brush("#7DD3FC"), FontWeight = FontWeights.SemiBold };
        var paymentsLabel = new TextBlock { Text = "دفعات العميل المسجلة", Foreground = Brush("#7DD3FC"), FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(paymentsLabel, 1);
        financePanel.Children.Add(partsLabel);
        financePanel.Children.Add(paymentsLabel);
        Grid.SetRow(_partsUsed, 1);
        financePanel.Children.Add(_partsUsed);
        Grid.SetRow(_payments, 1);
        Grid.SetColumn(_payments, 1);
        financePanel.Children.Add(_payments);
        Grid.SetRow(financePanel, 2);
        Grid.SetColumnSpan(financePanel, 4);
        editor.Children.Add(financePanel);

        var actions = new WrapPanel();
        actions.Children.Add(Button("حفظ تغييرات العملية", async (_, _) => await SaveOrderAsync()));
        actions.Children.Add(Button("إضافة ملاحظة للسجل", async (_, _) => await AddNoteAsync()));
        actions.Children.Add(Button("صرف قطعة من المخزون", async (_, _) => await OpenPartsUsageAsync()));
        actions.Children.Add(Button("تقرير للطباعة", async (_, _) => await ExportSelectedHtmlAsync()));
        actions.Children.Add(Button("إيصال استلام", async (_, _) => await ExportSelectedDocumentAsync(WorkshopDocumentKind.IntakeReceipt)));
        actions.Children.Add(Button("عرض سعر", async (_, _) => await ExportSelectedDocumentAsync(WorkshopDocumentKind.EstimateQuote)));
        actions.Children.Add(Button("فاتورة خدمة", async (_, _) => await ExportSelectedDocumentAsync(WorkshopDocumentKind.ServiceInvoice)));
        actions.Children.Add(Button("موافقة العميل على العرض", async (_, _) => await RecordQuoteDecisionAsync(CustomerQuoteDecision.Approved)));
        actions.Children.Add(Button("رفض العميل للعرض", async (_, _) => await RecordQuoteDecisionAsync(CustomerQuoteDecision.Rejected)));
        actions.Children.Add(Button("تسجيل دفعة", async (_, _) => await RecordPaymentAsync()));
        actions.Children.Add(Button("إيصال الدفعة المحددة", async (_, _) => await ExportSelectedPaymentReceiptAsync()));
        Grid.SetRow(_partsCost, 3);
        actions.Children.Add(_partsCost);
        Grid.SetRow(actions, 3);
        Grid.SetColumnSpan(actions, 4);
        editor.Children.Add(actions);
        Grid.SetRow(editor, 2);
        page.Children.Add(editor);
        return page;
    }

    private UIElement BuildSettingsPage()
    {
        var panel = new StackPanel { Margin = new Thickness(14), MaxWidth = 1150 };
        panel.Children.Add(new TextBlock { Text = "بيانات المنشأة والفوترة وإعدادات التقارير", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brush("#7DD3FC") });
        panel.Children.Add(new TextBlock
        {
            Text = "يمكنك ضبط بيانات مركز الصيانة لأي دولة. رمز العملة يجب أن يكون 3 أحرف ISO 4217، وLocale يحدد تنسيق الأرقام والتواريخ، وTime zone ID يحدد المنطقة الزمنية. لا يقوم البرنامج بتحويل العملات أو تخمين ضرائب بلدك.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("#B7C9DE"), Margin = new Thickness(0, 8, 0, 12)
        });
        var fields = new WrapPanel();
        fields.Children.Add(Field("اسم المركز", _businessName));
        fields.Children.Add(Field("الدولة", _country));
        fields.Children.Add(Field("العنوان", _address));
        fields.Children.Add(Field("الهاتف مع مفتاح الدولة", _phone));
        fields.Children.Add(Field("البريد الإلكتروني", _email));
        fields.Children.Add(Field("الرقم الضريبي", _tax));
        fields.Children.Add(Field("Currency code", _currency));
        fields.Children.Add(Field("Culture / Locale", _culture));
        fields.Children.Add(Field("Time zone ID", _timeZone));
        fields.Children.Add(Field("لغة التقارير ar/en", _reportLanguage));
        fields.Children.Add(Field("الضمان الافتراضي (شهر)", _defaultWarranty));
        panel.Children.Add(fields);
        panel.Children.Add(Button("حفظ إعدادات المركز", async (_, _) => await SaveSettingsAsync()));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement BuildExportsPage()
    {
        var panel = new StackPanel { Margin = new Thickness(18), MaxWidth = 900 };
        panel.Children.Add(new TextBlock { Text = "تصدير قابل للتكامل مع أنظمة المحاسبة والمكاتب", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brush("#7DD3FC") });
        panel.Children.Add(new TextBlock
        {
            Text = "تقارير HTML قابلة للطباعة أو الحفظ PDF من المتصفح، وملفات CSV UTF-8 لفتحها في Excel أو Google Sheets أو أنظمة ERP. كل ملف يوضح رمز العملة. لا تتم مشاركة البيانات تلقائيًا.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("#B7C9DE"), Margin = new Thickness(0, 8, 0, 15)
        });
        panel.Children.Add(new TextBlock { Text = "تحليلات التشغيل الفعلية", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brush("#7DD3FC"), Margin = new Thickness(0, 10, 0, 6) });
        panel.Children.Add(new TextBlock
        {
            Text = "تعتمد المؤشرات على السجلات المحلية المحفوظة. الإيراد محسوب من قيمة الأوامر المسلمة فقط، ولا يمثل الربح أو التحصيل النقدي أو الضرائب.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brush("#B7C9DE"), Margin = new Thickness(0, 4, 0, 8)
        });
        panel.Children.Add(_analyticsSummary);
        _analyticsFaults.Columns.Add(Column("وصف العطل المسجل", nameof(WorkshopFrequency.Label), 2));
        _analyticsFaults.Columns.Add(Column("العدد", nameof(WorkshopFrequency.Count), .5));
        _analyticsModels.Columns.Add(Column("موديل الجهاز", nameof(WorkshopFrequency.Label), 2));
        _analyticsModels.Columns.Add(Column("عدد الأوامر", nameof(WorkshopFrequency.Count), .5));
        _analyticsTechnicians.Columns.Add(Column("الفني", nameof(WorkshopTechnicianMetric.TechnicianName), 1.2));
        _analyticsTechnicians.Columns.Add(Column("الأوامر", nameof(WorkshopTechnicianMetric.AssignedOrders), .6));
        _analyticsTechnicians.Columns.Add(Column("المسلمة", nameof(WorkshopTechnicianMetric.DeliveredOrders), .6));
        _analyticsTechnicians.Columns.Add(Column("إيراد الأوامر المسلمة", nameof(WorkshopTechnicianMetric.RecordedDeliveredRevenue), 1));
        _analyticsMonthly.Columns.Add(Column("الشهر UTC", nameof(WorkshopMonthlyRevenue.Month), .8));
        _analyticsMonthly.Columns.Add(Column("الأوامر المسلمة", nameof(WorkshopMonthlyRevenue.DeliveredOrders), .8));
        _analyticsMonthly.Columns.Add(Column("الإيراد المسجل", nameof(WorkshopMonthlyRevenue.RecordedRevenue), 1));
        panel.Children.Add(AnalyticsGrid("أوصاف الأعطال الأكثر تكرارًا", _analyticsFaults, 170));
        panel.Children.Add(AnalyticsGrid("أكثر الموديلات استقبالًا", _analyticsModels, 150));
        panel.Children.Add(AnalyticsGrid("أداء الفنيين حسب السجلات", _analyticsTechnicians, 190));
        panel.Children.Add(AnalyticsGrid("الإيراد المسجل شهريًا", _analyticsMonthly, 220));
        panel.Children.Add(Button("تصدير التحليلات CSV", async (_, _) => await ExportAnalyticsCsvAsync()));
        panel.Children.Add(new TextBlock { Text = "تصدير ونسخ البيانات", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brush("#7DD3FC"), Margin = new Thickness(0, 18, 0, 6) });
        panel.Children.Add(Button("تصدير كل أوامر الصيانة CSV", async (_, _) => await ExportOrdersCsvAsync()));
        panel.Children.Add(Button("تصدير المخزون CSV", async (_, _) => await ExportInventoryCsvAsync()));
        panel.Children.Add(Button("نسخة احتياطية كاملة", async (_, _) => await BackupAsync()));
        panel.Children.Add(Button("استعادة نسخة احتياطية", async (_, _) => await RestoreAsync()));
        panel.Children.Add(new TextBlock { Text = "مسار قاعدة البيانات المحلي:\n" + _store.DatabasePath, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 0), Foreground = Brush("#8FA9C5") });
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async Task RefreshAsync()
    {
        _data = await _store.GetSnapshotAsync();
        var settings = _data.Settings ?? new WorkshopSettings();
        _businessName.Text = settings.BusinessName;
        _country.Text = settings.Country;
        _address.Text = settings.Address;
        _phone.Text = settings.Phone;
        _email.Text = settings.Email;
        _tax.Text = settings.TaxRegistrationNumber;
        _currency.Text = settings.CurrencyCode;
        _culture.Text = settings.CultureName;
        _timeZone.Text = settings.TimeZoneId;
        _defaultWarranty.Text = settings.DefaultWarrantyMonths.ToString();
        _reportLanguage.SelectedItem = settings.ReportLanguage;
        ApplyFilter();
        var open = _data.RepairOrders.Count(x => x.Status != "تم التسليم" && x.Status != "ملغي");
        var delivered = _data.RepairOrders.Count(x => x.Status == "تم التسليم");
        var activeWarranty = _data.RepairOrders.Count(x => WorkshopInternationalRules.IsWarrantyActive(x.WarrantyEndsAtUtc, DateTimeOffset.UtcNow));
        var lowStock = _data.Inventory.Count(x => x.NeedsRestock);
        RefreshAnalytics();
        _dashboard.Text = "مفتوح: " + open + "  |  تم التسليم: " + delivered + "  |  ضمان سارٍ: " + activeWarranty + "  |  مخزون منخفض: " + lowStock + "  |  العملة: " + settings.CurrencyCode;
        SetResult("تم تحديث بيانات المركز.");
    }

    private void ApplyFilter()
    {
        if (_orders is null) return;
        var query = (_search.Text ?? "").Trim();
        var statusValue = _statusFilter.SelectedItem as string;
        IEnumerable<RepairOrder> rows = _data.RepairOrders;
        if (!string.IsNullOrWhiteSpace(query))
            rows = rows.Where(x =>
                x.TicketNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.CustomerPhone.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.DeviceModel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.IntakeSymptoms.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(statusValue) && statusValue != "كل الحالات")
            rows = rows.Where(x => x.Status == statusValue);
        _orders.ItemsSource = rows.OrderByDescending(x => x.ReceivedAtUtc).ToList();
    }

    private void SelectOrder()
    {
        _selectedOrder = _orders.SelectedItem as RepairOrder;
        if (_selectedOrder is null) return;
        _status.SelectedItem = _selectedOrder.Status;
        _technician.Text = _selectedOrder.TechnicianName;
        _warrantyMonths.Text = _selectedOrder.WarrantyMonths.ToString();
        _before.Text = _selectedOrder.FindingsBeforeRepair;
        _work.Text = _selectedOrder.WorkPerformed;
        _after.Text = _selectedOrder.ResultsAfterRepair;
        _finalCost.Text = _selectedOrder.FinalCostEgp.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
        _timeline.ItemsSource = (_selectedOrder.Timeline ?? []).OrderByDescending(x => x.AtUtc).ToList();
        _partsUsed.ItemsSource = (_selectedOrder.PartsUsed ?? []).OrderByDescending(x => x.UsedAtUtc).ToList();
        _payments.ItemsSource = (_selectedOrder.Payments ?? []).OrderByDescending(x => x.ReceivedAtUtc).ToList();
        var currency = WorkshopInternationalRules.NormalizeCurrencyCode(_data.Settings.CurrencyCode);
        _partsCost.Text = $"تكلفة قطع الغيار المسجلة: {_selectedOrder.PartsCostEgp:0.00} {currency}";
        _financeSummary.Text =
            $"موافقة العرض: {_selectedOrder.PaymentStatus}  |  قيمة العرض المعتمدة: {WorkshopInternationalRules.FormatMoney(_selectedOrder.PaymentTargetEgp, currency, _data.Settings.CultureName)}\n" +
            $"المدفوع: {WorkshopInternationalRules.FormatMoney(_selectedOrder.TotalPaidEgp, currency, _data.Settings.CultureName)}  |  المتبقي: {WorkshopInternationalRules.FormatMoney(_selectedOrder.OutstandingBalanceEgp, currency, _data.Settings.CultureName)}";
    }

    private async Task OpenPartsUsageAsync()
    {
        if (_selectedOrder is null)
        {
            SetResult("اختر أمر صيانة أولًا.");
            return;
        }
        if (_selectedOrder.Status is "تم التسليم" or "ملغي")
        {
            SetResult("لا يمكن صرف قطع لأمر تم تسليمه أو إلغاؤه.");
            return;
        }

        var orderId = _selectedOrder.Id;
        var window = new PartsUsageWindow(_store, _selectedOrder, async () =>
        {
            await RefreshAsync();
            _orders.SelectedItem = _data.RepairOrders.FirstOrDefault(order => order.Id == orderId);
        })
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private async Task SaveOrderAsync()
    {
        if (_selectedOrder is null) { SetResult("اختر أمر صيانة أولًا."); return; }
        try
        {
            _selectedOrder.Status = _status.SelectedItem as string ?? _selectedOrder.Status;
            _selectedOrder.TechnicianName = _technician.Text;
            _selectedOrder.WarrantyMonths = ParseInt(_warrantyMonths.Text);
            _selectedOrder.FindingsBeforeRepair = _before.Text;
            _selectedOrder.WorkPerformed = _work.Text;
            _selectedOrder.ResultsAfterRepair = _after.Text;
            _selectedOrder.FinalCostEgp = ParseMoney(_finalCost.Text);
            await _store.UpdateRepairOrderAsync(_selectedOrder);
            var id = _selectedOrder.Id;
            await RefreshAsync();
            _orders.SelectedItem = _data.RepairOrders.FirstOrDefault(x => x.Id == id);
            SetResult("تم حفظ العملية وإعادة حساب تاريخ الضمان عند التسليم.");
        }
        catch (Exception ex) { SetResult("تعذر الحفظ: " + ex.Message); }
    }

    private async Task AddNoteAsync()
    {
        if (_selectedOrder is null) { SetResult("اختر أمر صيانة أولًا."); return; }
        var input = TextEntry(this, "ملاحظة المتابعة الفنية", "اكتب الإجراء أو القياس أو النتيجة الفعلية...");
        if (input is null || string.IsNullOrWhiteSpace(input)) return;
        try
        {
            var id = _selectedOrder.Id;
            await _store.AddTimelineNoteAsync(id, _technician.Text, input);
            await RefreshAsync();
            _orders.SelectedItem = _data.RepairOrders.FirstOrDefault(x => x.Id == id);
            SetResult("تمت إضافة سجل المتابعة.");
        }
        catch (Exception ex) { SetResult("تعذرت إضافة الملاحظة: " + ex.Message); }
    }

    private async Task RecordQuoteDecisionAsync(CustomerQuoteDecision decision)
    {
        if (_selectedOrder is null) { SetResult("اختر أمر صيانة أولًا."); return; }
        var technician = (_technician.Text ?? "").Trim();
        if (technician.Length < 2)
        {
            SetResult("اكتب اسم الفني المسؤول أولًا لتوثيق قرار العميل.");
            return;
        }

        var contactMethod = TextEntry(this, "طريقة توثيق التواصل", "مثال: حضور العميل بالمركز / مكالمة هاتفية / رسالة موثقة");
        if (contactMethod is null) return;
        var note = TextEntry(this, "ملاحظة قرار العميل (اختياري)", "اكتب أي شروط أو ملاحظات ذكرها العميل...");
        if (note is null) return;

        var decisionLabel = decision == CustomerQuoteDecision.Approved ? "موافقة" : "رفض";
        var amount = WorkshopInternationalRules.FormatMoney(
            _selectedOrder.CurrentQuoteAmountEgp,
            WorkshopInternationalRules.NormalizeCurrencyCode(_data.Settings.CurrencyCode),
            _data.Settings.CultureName);
        if (MessageBox.Show(
            this,
            $"سيتم تسجيل {decisionLabel} العميل على عرض السعر {amount} عبر: {contactMethod}. هذا سجل يدوي وليس توقيعًا إلكترونيًا. هل تؤكد؟",
            "تأكيد قرار العميل",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var id = _selectedOrder.Id;
        try
        {
            await _store.RecordQuoteDecisionAsync(id, decision, contactMethod, technician, note);
            await RefreshAsync();
            _orders.SelectedItem = _data.RepairOrders.FirstOrDefault(item => item.Id == id);
            SetResult($"تم حفظ قرار العميل: {decisionLabel}. القرار محفوظ كسجل تاريخي ولا يمثل توقيعًا إلكترونيًا.");
        }
        catch (Exception ex) { SetResult("تعذر تسجيل قرار العميل: " + ex.Message); }
    }

    private async Task RecordPaymentAsync()
    {
        if (_selectedOrder is null) { SetResult("اختر أمر صيانة أولًا."); return; }
        if (!_selectedOrder.HasCurrentQuoteApproval)
        {
            SetResult("لا يمكن تسجيل دفعة قبل اعتماد قيمة العرض الحالية من العميل.");
            return;
        }
        var technician = (_technician.Text ?? "").Trim();
        if (technician.Length < 2)
        {
            SetResult("اكتب اسم الفني المسؤول أولًا لتوثيق الدفعة.");
            return;
        }

        var amountText = TextEntry(this, "قيمة الدفعة", "اكتب مبلغ الدفعة بالأرقام وبحد أقصى منزلتين عشريتين.");
        if (amountText is null) return;
        if (!decimal.TryParse(amountText, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out var amount) &&
            !decimal.TryParse(amountText, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out amount))
        {
            SetResult("قيمة الدفعة غير صالحة.");
            return;
        }
        var method = TextEntry(this, "طريقة الدفع", "مثال: نقدًا / تحويل بنكي / محفظة إلكترونية");
        if (method is null) return;
        var reference = TextEntry(this, "مرجع الدفع (اختياري)", "رقم إيصال أو مرجع التحويل إن وجد...");
        if (reference is null) return;
        var note = TextEntry(this, "ملاحظة الدفعة (اختياري)", "ملاحظة داخلية عن الدفعة...");
        if (note is null) return;

        var currency = WorkshopInternationalRules.NormalizeCurrencyCode(_data.Settings.CurrencyCode);
        if (MessageBox.Show(
            this,
            $"سيتم تسجيل دفعة {WorkshopInternationalRules.FormatMoney(amount, currency, _data.Settings.CultureName)} بطريقة {method}. الرصيد الحالي {_selectedOrder.OutstandingBalanceEgp:0.00} {currency}. التسجيل يدوي ولا يتحقق من وصول الأموال لمزود الدفع. هل تؤكد؟",
            "تأكيد تسجيل الدفعة",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var id = _selectedOrder.Id;
        try
        {
            var payment = await _store.RecordPaymentAsync(id, amount, method, technician, reference, note);
            await RefreshAsync();
            _orders.SelectedItem = _data.RepairOrders.FirstOrDefault(item => item.Id == id);
            SetResult($"تم تسجيل الدفعة. الرصيد بعد الدفعة: {payment.BalanceAfterPaymentEgp:0.00} {currency}. لم يتم التحقق خارجيًا من التحصيل.");
        }
        catch (Exception ex) { SetResult("تعذر تسجيل الدفعة: " + ex.Message); }
    }

    private async Task ExportSelectedPaymentReceiptAsync()
    {
        if (_selectedOrder is null) { SetResult("اختر أمر صيانة أولًا."); return; }
        if (_payments.SelectedItem is not RepairPaymentRecord payment)
        {
            SetResult("حدد دفعة من سجل الدفعات أولًا.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Payment record receipt",
            Filter = "HTML|*.html",
            FileName = _selectedOrder.TicketNumber + "-PAY-" + payment.Id.ToString("N")[..8] + ".html"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await WorkshopServiceDocumentExporter.ExportPaymentReceiptAsync(_data, _selectedOrder, payment, dialog.FileName);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            SetResult("تم إنشاء إيصال سجل الدفعة وفتحه للطباعة. الإيصال لا يمثل تحققًا من مزود الدفع.");
        }
        catch (Exception ex) { SetResult("تعذر تصدير إيصال الدفعة: " + ex.Message); }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new WorkshopSettings
            {
                BusinessName = _businessName.Text,
                Country = _country.Text,
                Address = _address.Text,
                Phone = _phone.Text,
                Email = _email.Text,
                TaxRegistrationNumber = _tax.Text,
                CurrencyCode = _currency.Text,
                CultureName = _culture.Text,
                TimeZoneId = _timeZone.Text,
                ReportLanguage = _reportLanguage.SelectedItem as string ?? "ar",
                DefaultWarrantyMonths = ParseInt(_defaultWarranty.Text)
            };
            await _store.SaveSettingsAsync(settings);
            await RefreshAsync();
            SetResult("تم حفظ إعدادات المركز العالمية.");
        }
        catch (Exception ex) { SetResult("تعذر حفظ الإعدادات: " + ex.Message); }
    }

    private async Task ExportSelectedHtmlAsync()
    {
        if (_selectedOrder is null) { SetResult("اختر عملية من القائمة أولًا."); return; }
        var dialog = new SaveFileDialog { Title = "Printable work order", Filter = "HTML|*.html", FileName = _selectedOrder.TicketNumber + ".html" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await WorkshopReportExporter.ExportOrderHtmlAsync(_data, _selectedOrder, dialog.FileName);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            SetResult("تم فتح التقرير. استخدم Ctrl+P للطباعة أو حفظ PDF.");
        }
        catch (Exception ex) { SetResult("تعذر تصدير التقرير: " + ex.Message); }
    }

    private void RefreshAnalytics()
    {
        var report = WorkshopAnalyticsService.Analyze(_data, DateTimeOffset.UtcNow);
        var settings = _data.Settings ?? new WorkshopSettings();
        var currency = WorkshopInternationalRules.NormalizeCurrencyCode(settings.CurrencyCode);
        var averageDuration = report.AverageDeliveryHours.HasValue
            ? report.AverageDeliveryHours.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ساعة"
            : "لا تتوفر مدة تسليم صالحة";

        _analyticsSummary.Text =
            $"إجمالي الأوامر: {report.TotalOrders}  |  مفتوح: {report.OpenOrders}  |  تم التسليم: {report.DeliveredOrders}  |  ملغي: {report.CancelledOrders}\n" +
            $"الإيراد المسجل للأوامر المسلمة: {WorkshopInternationalRules.FormatMoney(report.RecordedDeliveredRevenue, currency, settings.CultureName)}  |  تقديرات الأعمال المفتوحة: {WorkshopInternationalRules.FormatMoney(report.EstimatedOpenWorkValue, currency, settings.CultureName)}\n" +
            $"تكلفة قطع الغيار المسجلة: {WorkshopInternationalRules.FormatMoney(report.RecordedPartsCost, currency, settings.CultureName)}  |  قيمة المخزون بالتكلفة: {WorkshopInternationalRules.FormatMoney(report.InventoryValueAtCost, currency, settings.CultureName)}  |  مخزون منخفض: {report.LowStockItems}\n" +
            $"الدفعات المسجلة يدويًا: {WorkshopInternationalRules.FormatMoney(report.TotalRecordedPayments, currency, settings.CultureName)}  |  أرصدة العروض المعتمدة المتبقية: {WorkshopInternationalRules.FormatMoney(report.ApprovedOutstandingBalance, currency, settings.CultureName)}\n" +
            $"متوسط المدة من الاستلام إلى التسليم: {averageDuration}";

        _analyticsFaults.ItemsSource = report.FrequentReportedFaults;
        _analyticsModels.ItemsSource = report.MostServicedModels;
        _analyticsTechnicians.ItemsSource = report.TechnicianPerformance;
        _analyticsMonthly.ItemsSource = report.MonthlyDeliveredRevenue;
    }

    private async Task ExportAnalyticsCsvAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export workshop analytics",
            Filter = "CSV|*.csv",
            FileName = "SoftBox-Workshop-Analytics.csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await WorkshopAnalyticsService.ExportCsvAsync(_data, dialog.FileName);
            SetResult("تم تصدير تحليلات الورشة الفعلية إلى CSV.");
        }
        catch (Exception ex) { SetResult("تعذر تصدير التحليلات: " + ex.Message); }
    }

    private async Task ExportSelectedDocumentAsync(WorkshopDocumentKind kind)
    {
        if (_selectedOrder is null)
        {
            SetResult("اختر أمر صيانة أولًا.");
            return;
        }

        var suffix = kind switch
        {
            WorkshopDocumentKind.IntakeReceipt => "RECEIPT",
            WorkshopDocumentKind.EstimateQuote => "ESTIMATE",
            WorkshopDocumentKind.ServiceInvoice => "INVOICE",
            _ => "DOCUMENT"
        };
        var dialog = new SaveFileDialog
        {
            Title = "Export workshop document",
            Filter = "HTML|*.html",
            FileName = _selectedOrder.TicketNumber + "-" + suffix + ".html"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await WorkshopServiceDocumentExporter.ExportAsync(_data, _selectedOrder, kind, dialog.FileName);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            SetResult("تم إنشاء المستند وفتحه. راجع البيانات ثم استخدم Ctrl+P للطباعة أو حفظ PDF.");
        }
        catch (Exception ex) { SetResult("تعذر إنشاء المستند: " + ex.Message); }
    }

    private async Task ExportOrdersCsvAsync()
    {
        var dialog = new SaveFileDialog { Title = "Export repair orders", Filter = "CSV|*.csv", FileName = "PhoneLab-Orders.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try { await WorkshopReportExporter.ExportOrdersCsvAsync(_data, dialog.FileName); SetResult("تم تصدير أوامر الصيانة إلى CSV."); }
        catch (Exception ex) { SetResult("تعذر التصدير: " + ex.Message); }
    }

    private async Task ExportInventoryCsvAsync()
    {
        var dialog = new SaveFileDialog { Title = "Export parts inventory", Filter = "CSV|*.csv", FileName = "PhoneLab-Inventory.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try { await WorkshopReportExporter.ExportInventoryCsvAsync(_data, dialog.FileName); SetResult("تم تصدير المخزون إلى CSV."); }
        catch (Exception ex) { SetResult("تعذر التصدير: " + ex.Message); }
    }

    private async Task BackupAsync()
    {
        var dialog = new SaveFileDialog { Title = "Encrypted SoftBox workshop backup", Filter = "SoftBox encrypted backup|*.sbxbackup", DefaultExt = ".sbxbackup", AddExtension = true, FileName = "SoftBox-Workshop-Backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".sbxbackup" };
        if (dialog.ShowDialog(this) != true) return;
        try { await _store.CreateBackupAsync(dialog.FileName); SetResult("تم إنشاء النسخة الاحتياطية."); }
        catch (Exception ex) { SetResult("فشل النسخ الاحتياطي: " + ex.Message); }
    }

    private async Task RestoreAsync()
    {
        var dialog = new OpenFileDialog { Title = "Restore encrypted or legacy workshop backup", Filter = "SoftBox backup (*.sbxbackup;*.json)|*.sbxbackup;*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "سيتم استبدال بيانات المركز الحالية بمحتويات النسخة. هل تريد المتابعة؟", "تأكيد الاستعادة", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await _store.RestoreBackupAsync(dialog.FileName); await RefreshAsync(); SetResult("تمت استعادة البيانات."); }
        catch (Exception ex) { SetResult("فشلت الاستعادة: " + ex.Message); }
    }

    private static string? TextEntry(Window owner, string title, string hint)
    {
        var dialog = new Window
        {
            Owner = owner, Title = title, Width = 520, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush("#0D1C2E"), Foreground = Brush("#EAF3FF"), ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(15) };
        panel.Children.Add(new TextBlock { Text = hint, Foreground = Brush("#A9C1DA"), Margin = new Thickness(4) });
        var input = Box(450, true);
        input.Height = 100;
        panel.Children.Add(input);
        var save = new Button { Content = "حفظ", Width = 100, Margin = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) => dialog.DialogResult = true;
        panel.Children.Add(save);
        dialog.Content = panel;
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private void SetResult(string message) => _result.Text = message;

    private static int ParseInt(string text) => string.IsNullOrWhiteSpace(text) ? 0 : int.Parse(text, System.Globalization.CultureInfo.CurrentCulture);
    private static decimal ParseMoney(string text) => string.IsNullOrWhiteSpace(text) ? 0 : decimal.Parse(text, System.Globalization.CultureInfo.CurrentCulture);

    private static UIElement AnalyticsGrid(string title, DataGrid grid, double height)
    {
        grid.Height = height;
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#B7DDF7"),
            Margin = new Thickness(5, 3, 5, 3)
        });
        panel.Children.Add(grid);
        return panel;
    }

    private static UIElement AddEditorField(Grid grid, string label, Control control, int column)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#9FB5CC"), FontSize = 11, Margin = new Thickness(4) });
        stack.Children.Add(control);
        Grid.SetColumn(stack, column);
        Grid.SetRow(stack, 0);
        grid.Children.Add(stack);
        return stack;
    }

    private static string[] Statuses() => new[] { "تم الاستلام", "قيد التشخيص", "بانتظار قطع غيار", "قيد الإصلاح", "جاهز للتسليم", "تم التسليم", "ملغي" };
    private static string[] StatusesWithAll() => new[] { "كل الحالات" }.Concat(Statuses()).ToArray();

    private static DataGrid GridView() => new()
    {
        AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true,
        SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow,
        Background = Brush("#0D1C2E"), Foreground = Brush("#EAF3FF"),
        RowBackground = Brush("#12243A"), AlternatingRowBackground = Brush("#172B43"),
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, Margin = new Thickness(0, 5, 0, 0)
    };

    private static DataGridTextColumn Column(string header, string property, double weight)
        => new() { Header = header, Binding = new Binding(property), Width = new DataGridLength(weight, DataGridLengthUnitType.Star), IsReadOnly = true };

    private static TextBox Box(double width, bool multiLine = false) => new()
    {
        Width = width, MinHeight = multiLine ? 65 : 34, Height = multiLine ? 65 : 34,
        AcceptsReturn = multiLine, TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalContentAlignment = VerticalAlignment.Center, Background = Brush("#0B192A"),
        Foreground = Brush("#F1F7FF"), BorderBrush = Brush("#36516F"), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(4)
    };

    private static UIElement Field(string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(3, 2, 3, 2) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("#9FB5CC"), FontSize = 11, Margin = new Thickness(5, 0, 0, 2) });
        panel.Children.Add(control);
        return panel;
    }

    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = text, Padding = new Thickness(13, 8, 13, 8), Margin = new Thickness(4),
            Background = Brush("#14517A"), Foreground = Brushes.White, BorderBrush = Brush("#2F83B5"),
            FontWeight = FontWeights.SemiBold, MinHeight = 34
        };
        button.Click += handler;
        return button;
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
