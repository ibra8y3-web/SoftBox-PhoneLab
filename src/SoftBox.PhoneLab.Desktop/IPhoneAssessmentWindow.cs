using System.Globalization;
using SoftBox.PhoneLab.Core.Activity;
using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

public enum IPhoneAssessmentMode
{
    PartsHistory,
    BatteryHealth,
    FunctionalTests
}

/// <summary>
/// Technician-assisted iPhone assessment. Only manually observed/officially displayed values are saved.
/// Nothing in this window pretends to automate a physical hardware test.
/// </summary>
public sealed class IPhoneAssessmentWindow : Window
{
    private readonly IPhoneDeviceInfoSnapshot _device;
    private readonly DiagnosticEvidenceStore _store;
    private readonly IPhoneAssessmentMode _mode;
    private readonly Dictionary<string, ComboBox> _choices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _notes = new(StringComparer.Ordinal);
    private readonly TextBox _maximumCapacity = new();
    private readonly TextBox _cycleCount = new();
    private readonly TextBox _manufactureDate = new();
    private readonly TextBox _firstUseDate = new();
    private ComboBox _batteryServiceState = new();
    private readonly TextBlock _status = new();
    private readonly Button _save = new();

    private static readonly (string Id, string Label)[] Parts =
    [
        ("battery", "البطارية"),
        ("display", "الشاشة"),
        ("rear-camera", "الكاميرا الخلفية"),
        ("front-camera", "الكاميرا الأمامية / TrueDepth"),
        ("logic-board", "اللوحة المنطقية")
    ];

    private static readonly (string Id, string Label)[] FunctionalChecks =
    [
        ("display-image", "الصورة والسطوع والبقع والبكسلات"),
        ("touch-grid", "اللمس والشبكة والحواف"),
        ("rear-camera", "الكاميرا الخلفية والتركيز"),
        ("front-camera", "الكاميرا الأمامية"),
        ("face-id", "Face ID / TrueDepth"),
        ("audio-speaker", "السماعة الخارجية وسماعة المكالمات"),
        ("microphone", "الميكروفونات وتسجيل الصوت"),
        ("sensors", "القرب والإضاءة والتسارع والجيروسكوب"),
        ("charging-port", "منفذ الشحن والكابل ونقل البيانات"),
        ("wireless-charging", "الشحن اللاسلكي والاتصال اللاسلكي")
    ];

    public IPhoneAssessmentWindow(
        IPhoneDeviceInfoSnapshot device,
        DiagnosticEvidenceStore store,
        IPhoneAssessmentMode mode)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _mode = mode;
        Title = mode switch
        {
            IPhoneAssessmentMode.PartsHistory => "SoftBox | سجل قطع iPhone",
            IPhoneAssessmentMode.BatteryHealth => "SoftBox | صحة بطارية iPhone",
            _ => "SoftBox | الفحص الوظيفي لـ iPhone"
        };
        Width = 920;
        Height = 780;
        MinWidth = 740;
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
        header.Children.Add(Text(Title[(Title.IndexOf('|') + 1)..].Trim(), "#7DD3FC", 23, FontWeights.Bold));
        header.Children.Add(Text($"{_device.DisplayTitle} • Serial {_device.MaskedSerialNumber}", "#91AFCB", 10));
        var warning = _mode switch
        {
            IPhoneAssessmentMode.PartsHistory =>
                "اقرأ الحالة مباشرة من iPhone: الإعدادات > عام > حول > سجل القطع والخدمة. قد لا يظهر السجل في كل الطرازات. عدم ظهور قطعة في القائمة لا يعني أنها أصلية أو لم تُستبدل.",
            IPhoneAssessmentMode.BatteryHealth =>
                "اقرأ القيم من إعدادات iPhone > البطارية > صحة البطارية. تختلف الحقول حسب الطراز وإصدار iOS. نسبة الشحن الحالية التي قرأها الاتصال ليست السعة القصوى ولا صحة البطارية.",
            _ =>
                "هذه اختبارات ينفذها الفني على الهاتف نفسه. لا يوجد فحص تلقائي يضغط الشاشة أو يشغّل Face ID أو يقيس المنفذ من الكمبيوتر؛ لا تسجل نجاحًا قبل تنفيذ الفحص فعليًا."
        };
        header.Children.Add(Notice(warning));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var content = new StackPanel();
        if (_mode == IPhoneAssessmentMode.PartsHistory)
            BuildPartsHistory(content);
        else if (_mode == IPhoneAssessmentMode.BatteryHealth)
            BuildBatteryHealth(content);
        else
            BuildFunctionalTests(content);
        scroll.Content = content;
        root.Children.Add(scroll);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#A9C4DE");
        footer.Children.Add(_status);
        _save.Content = "حفظ الأدلة في السجل المشفّر";
        _save.Padding = new Thickness(14, 8, 14, 8);
        _save.HorizontalAlignment = HorizontalAlignment.Right;
        _save.Background = Brush("#14517A");
        _save.Foreground = Brushes.White;
        _save.BorderBrush = Brush("#3A668B");
        _save.Click += async (_, _) => await SaveEvidenceAsync();
        footer.Children.Add(_save);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        return root;
    }

    private void BuildPartsHistory(Panel root)
    {
        root.Children.Add(Text("اختر الحالة الظاهرة لكل قطعة داخل سجل القطع والخدمة في iPhone:", "#DCEBFA", 12, FontWeights.SemiBold));
        foreach (var part in Parts)
            AddChoiceRow(root, part.Id, part.Label,
            [
                ("not-recorded", "غير مسجلة / لا يظهر سجل"),
                ("genuine", "Genuine — قطعة أصلية معترف بها من Apple"),
                ("used", "Used — قطعة مستخدمة سابقًا"),
                ("unknown", "Unknown — قطعة غير معروفة / غير متحققة"),
                ("unverified", "Unverified — جزء/لوحة غير متحققة"),
                ("finish-repair", "Finish Repair — يلزم إكمال الإصلاح")
            ]);
        root.Children.Add(Notice("تفسير الحالات: Genuine وUsed وUnknown وUnverified وFinish Repair تُحفظ كما قرأها الفني. لا نستنتج استبدالًا عند غياب السجل، ولا ندعي أن السجل يغطي كل تاريخ الصيانة."));
    }

    private void BuildBatteryHealth(Panel root)
    {
        root.Children.Add(Text("أدخل القيم الظاهرة على iPhone نفسه. الحقول الاختيارية تظل فارغة إذا لم يعرضها الطراز.", "#DCEBFA", 12, FontWeights.SemiBold));
        AddInput(root, "السعة القصوى للبطارية (%)", _maximumCapacity, "مثال: 87");
        AddInput(root, "عدد دورات الشحن", _cycleCount, "إذا كان ظاهرًا في الإعدادات");
        AddInput(root, "تاريخ التصنيع", _manufactureDate, "اختياري");
        AddInput(root, "أول استخدام", _firstUseDate, "اختياري");
        root.Children.Add(Text("رسالة صحة البطارية / الخدمة", "#7DD3FC", 10, FontWeights.SemiBold));
        _batteryServiceState = Choice(
        [
            ("not-recorded", "لم يتم التحقق"),
            ("normal", "Normal / لا توجد رسالة خدمة"),
            ("service", "Service / يوصى بالخدمة"),
            ("unable-to-verify", "Unable to Verify / تعذر التحقق من البطارية")
        ]);
        root.Children.Add(_batteryServiceState);
        root.Children.Add(Notice("لن يتم حساب السعة القصوى من نسبة الشحن الحالية. قيمة السعة القصوى أو عدد الدورات يجب أن تُقرأ من إعدادات iPhone أو تقرير خدمة موثوق."));
    }

    private void BuildFunctionalTests(Panel root)
    {
        root.Children.Add(Text("سجّل نتيجة كل اختبار بعد تنفيذه على الهاتف، واكتب ملاحظة عند اختيار ناجح أو فشل أو يحتاج مراجعة.", "#DCEBFA", 12, FontWeights.SemiBold));
        foreach (var check in FunctionalChecks)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 7) };
            panel.Children.Add(Text(check.Label, "#BFE5FA", 11, FontWeights.SemiBold));
            AddChoiceRow(panel, check.Id, "",
            [
                ("not-tested", "لم يُختبر"),
                ("pass", "ناجح"),
                ("failed", "فشل"),
                ("review", "يحتاج مراجعة")
            ], addLabel: false);
            var note = new TextBox { MinHeight = 42, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, ToolTip = "سجّل الملاحظة أو الدليل الذي شاهدته" };
            StyleInput(note);
            _notes[check.Id] = note;
            panel.Children.Add(note);
            root.Children.Add(Panel(panel));
        }
    }

    private void AddChoiceRow(
        Panel parent,
        string id,
        string label,
        (string Value, string Label)[] options,
        bool addLabel = true)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 4, 0, 5) };
        if (addLabel)
        {
            var title = Text(label, "#B7C9DE", 10, FontWeights.SemiBold);
            DockPanel.SetDock(title, Dock.Right);
            panel.Children.Add(title);
        }
        var choice = Choice(options);
        _choices[id] = choice;
        panel.Children.Add(choice);
        parent.Children.Add(Panel(panel));
    }

    private void AddInput(Panel parent, string label, TextBox input, string hint)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 5, 0, 7) };
        panel.Children.Add(Text(label, "#B7C9DE", 10, FontWeights.SemiBold));
        input.ToolTip = hint;
        StyleInput(input);
        panel.Children.Add(input);
        parent.Children.Add(panel);
    }

    private async Task SaveEvidenceAsync()
    {
        _save.IsEnabled = false;
        try
        {
            var components = _mode switch
            {
                IPhoneAssessmentMode.PartsHistory => BuildPartEvidence(),
                IPhoneAssessmentMode.BatteryHealth => BuildBatteryEvidence(),
                _ => BuildFunctionalEvidence()
            };
            if (components.Count == 0)
                throw new InvalidOperationException("لم يتم إدخال أي نتيجة قابلة للحفظ. اترك العناصر غير المختبرة كما هي.");
            var source = _mode switch
            {
                IPhoneAssessmentMode.PartsHistory => "Technician read iPhone Settings > General > About > Parts & Service History",
                IPhoneAssessmentMode.BatteryHealth => "Technician read iPhone Settings > Battery > Battery Health",
                _ => "Technician-performed physical iPhone component checks"
            };
            var added = await _store.RecordSnapshotAsync(
                _device.EvidenceIdentity, "Apple", _device.ProductType, components, source);
            _status.Text = $"تم حفظ {added} سجل دليل جديد. حُفظت بصمة الجهاز والرقم التسلسلي المقنّع فقط.";
            MessageBox.Show(this, _status.Text, "SoftBox • سجل الأدلة", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _status.Text = "لم يتم حفظ الأدلة: " + ex.Message;
            MessageBox.Show(this, _status.Text, "SoftBox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _save.IsEnabled = true;
        }
    }

    private List<ComponentDiagnostic> BuildPartEvidence()
    {
        var results = new List<ComponentDiagnostic>();
        foreach (var part in Parts)
        {
            var value = SelectedValue(_choices[part.Id]);
            if (value == "not-recorded")
                continue;
            var status = value == "genuine" ? DiagnosticStatus.DataRead : DiagnosticStatus.Review;
            var label = _choices[part.Id].SelectedItem is ComboBoxItem selected ? selected.Content?.ToString() : value;
            results.Add(new ComponentDiagnostic
            {
                Id = "iphone-part-" + part.Id,
                Name = part.Label,
                Status = status,
                Evidence = "الحالة التي سجلها الفني من إعدادات iPhone: " + label,
                Description = "سجل Apple الظاهر على الجهاز. عدم وجود سجل لا يثبت أن القطعة لم تُستبدل."
            });
        }
        return results;
    }

    private List<ComponentDiagnostic> BuildBatteryEvidence()
    {
        int? capacity = ParseOptionalInteger(_maximumCapacity.Text, "السعة القصوى", 1, 100);
        int? cycles = ParseOptionalInteger(_cycleCount.Text, "عدد الدورات", 0, 10000);
        var state = SelectedValue(_batteryServiceState);
        if (capacity is null && cycles is null && state == "not-recorded" &&
            string.IsNullOrWhiteSpace(_manufactureDate.Text) && string.IsNullOrWhiteSpace(_firstUseDate.Text))
            return [];

        var status = state is "service" or "unable-to-verify" || capacity is <= 80
            ? DiagnosticStatus.Review
            : DiagnosticStatus.DataRead;
        var evidence =
            $"MaximumCapacity={capacity?.ToString(CultureInfo.InvariantCulture) ?? "غير متاح"}%; " +
            $"CycleCount={cycles?.ToString(CultureInfo.InvariantCulture) ?? "غير متاح"}; " +
            $"ManufactureDate={SafeNote(_manufactureDate.Text)}; FirstUse={SafeNote(_firstUseDate.Text)}; " +
            $"ServiceState={state}. Values manually read from iPhone Settings.";
        return
        [
            new ComponentDiagnostic
            {
                Id = "iphone-battery-health",
                Name = "صحة البطارية ودورات الشحن",
                Status = status,
                Evidence = evidence,
                Description = "بيانات رسمية أدخلها الفني من الجهاز. لم يتم تقدير صحة البطارية من نسبة الشحن الحالية."
            }
        ];
    }

    private List<ComponentDiagnostic> BuildFunctionalEvidence()
    {
        var results = new List<ComponentDiagnostic>();
        foreach (var check in FunctionalChecks)
        {
            var value = SelectedValue(_choices[check.Id]);
            if (value == "not-tested")
                continue;
            var note = SafeNote(_notes[check.Id].Text);
            if (string.IsNullOrWhiteSpace(note))
                throw new InvalidOperationException($"أضف ملاحظة دليل لاختبار «{check.Label}» قبل حفظ النتيجة.");
            results.Add(new ComponentDiagnostic
            {
                Id = "iphone-check-" + check.Id,
                Name = check.Label,
                Status = value switch
                {
                    "pass" => DiagnosticStatus.Pass,
                    "failed" => DiagnosticStatus.Failed,
                    "review" => DiagnosticStatus.Review,
                    _ => DiagnosticStatus.NotTested
                },
                Evidence = note,
                Description = "نتيجة أدخلها الفني بعد فحص فعلي للهاتف؛ لم تنفذ تلقائيًا بواسطة SoftBox."
            });
        }
        return results;
    }

    private static int? ParseOptionalInteger(string value, string name, int minimum, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var result) ||
            result < minimum || result > maximum)
            throw new ArgumentOutOfRangeException(name, $"{name}: القيمة يجب أن تكون بين {minimum} و{maximum}.");
        return result;
    }

    private static string SafeNote(string value)
    {
        var result = (value ?? string.Empty).Trim();
        if (result.Length > 1000 || result.Any(char.IsControl) && result.Contains('\0'))
            throw new ArgumentException("الملاحظة أطول من الحد المسموح أو تحتوي محارف غير صالحة.");
        return result;
    }

    private static string SelectedValue(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "not-tested";

    private static ComboBox Choice((string Value, string Label)[] options)
    {
        var combo = new ComboBox
        {
            MinWidth = 250,
            Padding = new Thickness(7, 5, 7, 5),
            Background = Brush("#10243A"),
            Foreground = Brush("#EAF3FF"),
            BorderBrush = Brush("#36516D"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var option in options)
            combo.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value, Foreground = Brush("#EAF3FF"), Background = Brush("#10243A") });
        combo.SelectedIndex = 0;
        return combo;
    }

    private static Border Notice(string text)
        => new()
        {
            Background = Brush("#30251B"),
            BorderBrush = Brush("#8B6238"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 10),
            Child = Text(text, "#FFE0B7", 10)
        };

    private static Border Panel(UIElement child)
        => new()
        {
            Background = Brush("#0F1E32"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9),
            Margin = new Thickness(0, 3, 0, 3),
            Child = child
        };

    private static void StyleInput(TextBox input)
    {
        input.Padding = new Thickness(8, 6, 8, 6);
        input.Background = Brush("#0A1726");
        input.Foreground = Brush("#EAF3FF");
        input.BorderBrush = Brush("#36516D");
        input.Margin = new Thickness(0, 4, 0, 4);
    }

    private static TextBlock Text(string value, string color, double size, FontWeight? weight = null)
        => new()
        {
            Text = value,
            Foreground = Brush(color),
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        };

    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value)!);
}
