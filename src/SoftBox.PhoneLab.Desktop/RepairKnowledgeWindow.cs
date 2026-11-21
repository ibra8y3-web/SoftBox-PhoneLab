using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Help;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Searchable offline repair knowledge authored by workshop technicians.
/// Entries are stored locally encrypted and are never presented as OEM facts unless the source is provided.
/// </summary>
public sealed class RepairKnowledgeWindow : Window
{
    private readonly RepairKnowledgeStore _store = new();
    private readonly ObservableCollection<KnowledgeListItem> _entries = [];
    private readonly ListBox _list = new();
    private readonly TextBox _search = new();
    private readonly TextBox _manufacturer = new();
    private readonly TextBox _model = new();
    private readonly TextBox _symptom = new();
    private readonly TextBox _diagnosis = new();
    private readonly TextBox _evidence = new();
    private readonly TextBox _steps = new();
    private readonly TextBox _source = new();
    private readonly TextBox _author = new();
    private readonly CheckBox _verified = new();
    private readonly TextBox _details = new();
    private readonly TextBlock _status = new();
    private readonly Button _addButton;
    private readonly Button _updateButton;
    private readonly Button _deleteButton;
    private Guid? _selectedEntryId;
    private IReadOnlyList<RepairKnowledgeEntry> _all = [];

    public RepairKnowledgeWindow(string? manufacturer = null, string? model = null)
    {
        _addButton = ButtonControl("إضافة ملاحظة جديدة", "#176044", Add_Click);
        _updateButton = ButtonControl("حفظ تعديلات الملاحظة", "#14517A", Update_Click);
        _deleteButton = ButtonControl("حذف الملاحظة", "#713B43", Delete_Click);
        _updateButton.IsEnabled = false;
        _deleteButton.IsEnabled = false;

        Title = "SoftBox | قاعدة المعرفة المحلية للإصلاح";
        Width = 1240;
        Height = 860;
        MinWidth = 980;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
        _manufacturer.Text = manufacturer ?? "";
        _model.Text = model ?? "";
        _search.Text = !string.IsNullOrWhiteSpace(model) ? model : manufacturer ?? "";
        _list.SelectionChanged += (_, _) => ShowSelected();
        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            if (!string.IsNullOrWhiteSpace(_search.Text))
                await FilterAsync();
            if (_entries.Count > 0 && _list.SelectedIndex < 0)
                _list.SelectedIndex = 0;
        };
        _search.TextChanged += async (_, _) => await FilterAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "قاعدة المعرفة المحلية للإصلاح",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "OFFLINE • DPAPI ENCRYPTED • TECHNICIAN AUTHORED • MODEL / SYMPTOM SEARCH",
            FontSize = 10,
            Foreground = Brush("#8199B5")
        });
        header.Children.Add(new TextBlock
        {
            Text = "تُحفظ الملاحظات التي يضيفها الفني فقط. لا يحتوي المخزن على Serial أو بيانات عملاء، وعلامة التحقق تعني أن الفني راجع المعلومة وليست اعتمادًا من الشركة المصنعة.",
            FontSize = 11,
            Foreground = Brush("#F0C9A5"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var form = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Configure(_manufacturer, "الشركة", 0, 0, form);
        Configure(_model, "رقم الموديل / Product", 1, 0, form);
        Configure(_symptom, "العَرَض أو الشكوى", 0, 1, form);
        Configure(_diagnosis, "التشخيص المدعوم بالدليل", 1, 1, form);
        Configure(_evidence, "الدليل أو القياس", 0, 2, form);
        Configure(_source, "مصدر المعلومة / رابط الدليل", 1, 2, form);
        Configure(_author, "اسم الفني", 0, 3, form);
        Configure(_steps, "خطوات الفحص الآمن — كل خطوة في سطر", 1, 3, form, multiline: true);
        _verified.Content = "راجعها الفني كمعلومة خبرة";
        _verified.Foreground = Brush("#DCEBFA");
        _verified.Margin = new Thickness(6);
        Grid.SetRow(_verified, 4);
        Grid.SetColumn(_verified, 0);
        form.Children.Add(_verified);
        var actions = new WrapPanel { Margin = new Thickness(4) };
        actions.Children.Add(_addButton);
        actions.Children.Add(_updateButton);
        actions.Children.Add(_deleteButton);
        Grid.SetRow(actions, 4);
        Grid.SetColumn(actions, 1);
        form.Children.Add(actions);
        DockPanel.SetDock(form, Dock.Top);
        root.Children.Add(new ScrollViewer { Content = form, MaxHeight = 290, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var searchLabel = new TextBlock { Text = "بحث في الشركة والموديل والعَرَض والتشخيص والدليل", Foreground = Brush("#9FC7E8"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(searchLabel, Dock.Right);
        searchPanel.Children.Add(searchLabel);
        _search.Height = 34;
        _search.Padding = new Thickness(8);
        _search.Background = Brush("#071625");
        _search.Foreground = Brush("#DCEBFA");
        _search.BorderBrush = Brush("#35516F");
        searchPanel.Children.Add(_search);
        DockPanel.SetDock(searchPanel, Dock.Top);
        root.Children.Add(searchPanel);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.9, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        _list.ItemsSource = _entries;
        _list.DisplayMemberPath = nameof(KnowledgeListItem.DisplayText);
        _list.Background = Brush("#071625");
        _list.Foreground = Brush("#DCEBFA");
        _list.BorderBrush = Brush("#263D58");
        _list.Padding = new Thickness(8);
        _list.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(_list, 0);
        body.Children.Add(_list);

        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.Wrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.Background = Brush("#071625");
        _details.Foreground = Brush("#DCEBFA");
        _details.BorderBrush = Brush("#263D58");
        _details.Padding = new Thickness(12);
        Grid.SetColumn(_details, 1);
        body.Children.Add(_details);
        root.Children.Add(body);

        _status.Margin = new Thickness(0, 10, 0, 0);
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private static void Configure(TextBox box, string hint, int column, int row, Grid grid, bool multiline = false)
    {
        box.ToolTip = hint;
        box.MinHeight = multiline ? 62 : 34;
        box.Margin = new Thickness(5);
        box.Padding = new Thickness(8);
        box.Background = Brush("#071625");
        box.Foreground = Brush("#DCEBFA");
        box.BorderBrush = Brush("#35516F");
        box.AcceptsReturn = multiline;
        box.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(box, column);
        Grid.SetRow(box, row);
        grid.Children.Add(box);
    }

    private async Task RefreshAsync()
    {
        try
        {
            _all = await _store.SearchAsync();
            await ApplyEntriesAsync(_all);
            _status.Text = $"قاعدة المعرفة: {_all.Count:N0} ملاحظة محلية مشفرة.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تحميل قاعدة المعرفة: " + ex.Message;
        }
    }

    private async Task FilterAsync()
    {
        try
        {
            var matches = await _store.SearchAsync(_search.Text);
            await ApplyEntriesAsync(matches);
        }
        catch (Exception ex) { _status.Text = "تعذر البحث في قاعدة المعرفة: " + ex.Message; }
    }

    private Task ApplyEntriesAsync(IReadOnlyList<RepairKnowledgeEntry> entries)
    {
        _entries.Clear();
        foreach (var entry in entries)
            _entries.Add(new KnowledgeListItem(entry));
        return Task.CompletedTask;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var entry = await _store.AddAsync(
                _manufacturer.Text, _model.Text, _symptom.Text, _diagnosis.Text,
                _evidence.Text, ReadSafeSteps(), _source.Text, _author.Text, _verified.IsChecked == true);
            _selectedEntryId = entry.Id;
            _search.Text = entry.Model;
            await RefreshAsync();
            _list.SelectedItem = _entries.FirstOrDefault(item => item.Entry.Id == entry.Id);
            LoadEntry(entry);
            _status.Text = "تم حفظ الملاحظة داخل قاعدة المعرفة المشفرة لهذا المستخدم.";
        }
        catch (Exception ex)
        {
            _status.Text = "لم تتم إضافة الملاحظة: " + ex.Message;
            MessageBox.Show(this, ex.Message, "قاعدة المعرفة", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntryId is not Guid id)
        {
            _status.Text = "اختر ملاحظة محفوظة من القائمة أولًا.";
            return;
        }

        try
        {
            var updatedEntry = await _store.UpdateAsync(
                id, _manufacturer.Text, _model.Text, _symptom.Text, _diagnosis.Text,
                _evidence.Text, ReadSafeSteps(), _source.Text, _author.Text, _verified.IsChecked == true);
            await RefreshAsync();
            _list.SelectedItem = _entries.FirstOrDefault(item => item.Entry.Id == updatedEntry.Id);
            LoadEntry(updatedEntry);
            _status.Text = "تم تحديث الملاحظة وحفظ التعديلات في قاعدة المعرفة المشفرة.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تحديث الملاحظة: " + ex.Message;
            MessageBox.Show(this, ex.Message, "قاعدة المعرفة", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntryId is not Guid id)
        {
            _status.Text = "اختر ملاحظة محفوظة من القائمة أولًا.";
            return;
        }
        if (MessageBox.Show(this, "سيتم حذف الملاحظة المحددة نهائيًا من قاعدة المعرفة المحلية. متابعة؟",
                "حذف ملاحظة الإصلاح", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            await _store.DeleteAsync(id);
            ClearEditor();
            _details.Clear();
            await RefreshAsync();
            _status.Text = "تم حذف الملاحظة المحددة من قاعدة المعرفة المشفرة.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر حذف الملاحظة: " + ex.Message;
        }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        ClearEditor();
        _list.SelectedItem = null;
        _details.Text = "ملاحظة جديدة. املأ الشركة والموديل والعَرَض والدليل والخطوات الآمنة ثم اضغط إضافة.";
        _status.Text = "جاهز لإضافة ملاحظة فنية جديدة.";
    }

    private string[] ReadSafeSteps()
        => _steps.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void ClearEditor()
    {
        _selectedEntryId = null;
        _manufacturer.Clear();
        _model.Clear();
        _symptom.Clear();
        _diagnosis.Clear();
        _evidence.Clear();
        _steps.Clear();
        _source.Clear();
        _author.Clear();
        _verified.IsChecked = false;
        _updateButton.IsEnabled = false;
        _deleteButton.IsEnabled = false;
    }

    private void LoadEntry(RepairKnowledgeEntry entry)
    {
        _selectedEntryId = entry.Id;
        _manufacturer.Text = entry.Manufacturer;
        _model.Text = entry.Model;
        _symptom.Text = entry.Symptom;
        _diagnosis.Text = entry.Diagnosis;
        _evidence.Text = entry.Evidence;
        _steps.Text = string.Join(Environment.NewLine, entry.SafeSteps);
        _source.Text = entry.Source;
        _author.Text = entry.Author;
        _verified.IsChecked = entry.TechnicianMarkedVerified;
        _updateButton.IsEnabled = true;
        _deleteButton.IsEnabled = true;
    }

    private void ShowSelected()
    {
        if (_list.SelectedItem is not KnowledgeListItem item)
            return;
        var entry = item.Entry;
        LoadEntry(entry);
        _details.Text = string.Join(Environment.NewLine,
        [
            "MANUFACTURER: " + entry.Manufacturer,
            "MODEL: " + entry.Model,
            "SYMPTOM: " + entry.Symptom,
            "DIAGNOSIS: " + entry.Diagnosis,
            "EVIDENCE: " + entry.Evidence,
            "SOURCE: " + entry.Source,
            "AUTHOR: " + entry.Author,
            "TECHNICIAN MARKED VERIFIED: " + entry.TechnicianMarkedVerified,
            "CREATED UTC: " + entry.CreatedAtUtc.ToString("O"),
            "UPDATED UTC: " + entry.UpdatedAtUtc.ToString("O"),
            "",
            "SAFE STEPS:",
            string.Join(Environment.NewLine, entry.SafeSteps.Select(step => "- " + step))
        ]);
    }

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(5),
            Background = Brush(background),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold
        };
        button.Click += handler;
        return button;
    }

    private sealed record KnowledgeListItem(RepairKnowledgeEntry Entry)
    {
        public string DisplayText => $"{Entry.Manufacturer} {Entry.Model} | {Entry.Symptom} {(Entry.TechnicianMarkedVerified ? "✓" : "•")}";
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
