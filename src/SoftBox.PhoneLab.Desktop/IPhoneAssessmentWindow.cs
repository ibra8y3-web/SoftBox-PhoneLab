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
    FunctionalTests,
    AuthenticityEvidence
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
    private readonly IPhoneBatteryDiagnosticsSnapshot? _batteryDiagnostics;
    private readonly Dictionary<string, ComboBox> _choices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _notes = new(StringComparer.Ordinal);
    private readonly TextBox _maximumCapacity = new();
    private readonly TextBox _cycleCount = new();
    private readonly TextBox _manufactureDate = new();
    private readonly TextBox _firstUseDate = new();
    private ComboBox _batteryServiceState = new();
    private ComboBox _coverageResult = new();
    private ComboBox _partsResult = new();
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
        IPhoneAssessmentMode mode,
        IPhoneBatteryDiagnosticsSnapshot? batteryDiagnostics = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _mode = mode;
        _batteryDiagnostics = batteryDiagnostics;
        Title = mode switch
        {
            IPhoneAssessmentMode.PartsHistory => "SoftBox | سجل قطع iPhone",
            IPhoneAssessmentMode.BatteryHealth => "SoftBox | صحة بطارية iPhone",
            IPhoneAssessmentMode.AuthenticityEvidence => "SoftBox | أدلة أصالة iPhone",
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
            IPhoneAssessmentMode.AuthenticityEvidence =>
                "هذه أداة تقييم قرائن وليست فاحصًا سحريًا للأصالة: استجابة Lockdown، وما يعرضه موقع Apple عند إدخال الرقم يدويًا، وسجل القطع الرسمي. قد يقلّد جهاز مقلد بعض بيانات النظام، لذلك لا تصدر حكمًا مطلقًا.",
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
        else if (_mode == IPhoneAssessmentMode.AuthenticityEvidence)
            BuildAuthenticityEvidence(content);
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
                ("unverified", "Unverified — لوحة منطقية سبق استبدالها ولم يتم التحقق منها"),
                ("finish-repair", "Finish Repair — يلزم إكمال الإصلاح")
            ]);
        root.Children.Add(Notice("تفسير الحالات: Genuine وUsed وUnknown وUnverified وFinish Repair تُحفظ كما قرأها الفني. يخص Unverified اللوحة المنطقية وفق وصف Apple. العناصر المتاحة تختلف حسب طراز iPhone وتاريخه. لا نستنتج استبدالًا عند غياب السجل، ولا ندعي أن السجل يغطي كل تاريخ الصيانة."));
    }

    private void BuildBatteryHealth(Panel root)
    {
        root.Children.Add(Text("قراءة آلية من Apple Diagnostics Relay (GasGauge)", "#7DD3FC", 12, FontWeights.Bold));
        if (_batteryDiagnostics is { } gauge)
        {
            root.Children.Add(Text(
                $"الحالة: {gauge.Status} • دورات الشحن: {gauge.CycleCount?.ToString(CultureInfo.InvariantCulture) ?? "غير متاحة"} • تقدير السعة القصوى: {gauge.EstimatedMaximumCapacityPercent?.ToString(CultureInfo.InvariantCulture) ?? "غير متاح"}%\n" +
                $"DesignCapacity الخام: {gauge.DesignCapacity?.ToString(CultureInfo.InvariantCulture) ?? "غير متاح"} • FullChargeCapacity الخام: {gauge.FullChargeCapacity?.ToString(CultureInfo.InvariantCulture) ?? "غير متاح"}\n" +
                $"طريقة التقدير: {gauge.EstimateBasis} • وقت القراءة UTC: {gauge.ReadAtUtc:O}",
                "#B7C9DE", 10));
        }
        else
        {
            root.Children.Add(Text("لم تتوفر قراءة GasGauge آلية. تأكد من وجود idevicediagnostics.exe أو سجّل القيم الظاهرة في إعدادات iPhone يدويًا.", "#F5D39E", 10));
        }
        root.Children.Add(Text("قيم إضافية من شاشة iPhone الرسمية (اختيارية)", "#7DD3FC", 11, FontWeights.SemiBold));
        AddInput(root, "السعة القصوى الظاهرة في الإعدادات (%)", _maximumCapacity, "مثال: 87");
        AddInput(root, "عدد دورات الشحن (تجاوز يدوي اختياري)", _cycleCount, "اتركه فارغًا لاستخدام قراءة GasGauge الآلية");
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
        root.Children.Add(Notice("نسبة الشحن الحالية ليست صحة البطارية. تقدير GasGauge قراءة تشخيصية وقد يختلف مقياس FullChargeCapacity حسب إصدار iOS؛ نقارن القيم المتوافقة فقط ونُظهر أساس التقدير. عند 80% أو أقل تظهر حالة «مراجعة» كتنبيه فحص أولي فقط، وليست أمر استبدال أو حكمًا نهائيًا؛ مرجع دورة التصميم يختلف بين الطرازات. أدخل القيمة الرسمية من الإعدادات عند توفرها."));
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
                IPhoneAssessmentMode.AuthenticityEvidence => BuildAuthenticityEvidenceSnapshot(),
                _ => BuildFunctionalEvidence()
            };
            if (components.Count == 0)
                throw new InvalidOperationException("لم يتم إدخال أي نتيجة قابلة للحفظ. اترك العناصر غير المختبرة كما هي.");
            var source = _mode switch
            {
                IPhoneAssessmentMode.PartsHistory => "Technician read iPhone Settings > General > About > Parts & Service History",
                IPhoneAssessmentMode.BatteryHealth => _batteryDiagnostics is null
                    ? "Apple iPhone Settings > Battery Health (technician-read values)"
                    : "Apple diagnostics relay GasGauge plus technician-read iPhone Battery Health settings",
                IPhoneAssessmentMode.AuthenticityEvidence => "Technician-reviewed Apple Lockdown identity, Apple coverage result and Parts & Service History",
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
        int? manualCycles = ParseOptionalInteger(_cycleCount.Text, "عدد الدورات", 0, 100000);
        long? cycles = manualCycles ?? _batteryDiagnostics?.CycleCount;
        int? diagnosticEstimate = _batteryDiagnostics?.EstimatedMaximumCapacityPercent;
        var state = SelectedValue(_batteryServiceState);
        if (capacity is null && cycles is null && diagnosticEstimate is null && state == "not-recorded" &&
            string.IsNullOrWhiteSpace(_manufactureDate.Text) && string.IsNullOrWhiteSpace(_firstUseDate.Text))
            return [];

        var effectiveCapacity = capacity ?? diagnosticEstimate;
        var status = state is "service" or "unable-to-verify" || effectiveCapacity is <= 80
            ? DiagnosticStatus.Review
            : DiagnosticStatus.DataRead;
        var evidence =
            $"SettingsMaximumCapacity={capacity?.ToString(CultureInfo.InvariantCulture) ?? "غير متاح"}%; " +
            $"GasGaugeEstimatedMaximumCapacity={diagnosticEstimate?.ToString(CultureInfo.InvariantCulture) ?? "غير متاح"}%; " +
            $"GasGaugeEstimateBasis={_batteryDiagnostics?.EstimateBasis ?? "Unavailable"}; " +
            $"CycleCount={cycles?.ToString(CultureInfo.InvariantCulture) ?? "غير متاح"}; " +
            $"DesignCapacityRaw={_batteryDiagnostics?.DesignCapacity?.ToString(CultureInfo.InvariantCulture) ?? "غير متاح"}; " +
            $"FullChargeCapacityRaw={_batteryDiagnostics?.FullChargeCapacity?.ToString(CultureInfo.InvariantCulture) ?? "غير متاح"}; " +
            $"GasGaugeStatus={_batteryDiagnostics?.Status ?? "Unavailable"}; " +
            $"ManufactureDate={SafeNote(_manufactureDate.Text)}; FirstUse={SafeNote(_firstUseDate.Text)}; " +
            $"ServiceState={state}. Diagnostic values came from the read-only Apple GasGauge relay; Settings values were entered by the technician.";
        return
        [
            new ComponentDiagnostic
            {
                Id = "iphone-battery-health",
                Name = "صحة البطارية ودورات الشحن",
                Status = status,
                Evidence = evidence,
                Description = "قراءة GasGauge التشخيصية وقيم أدخلها الفني من شاشة iPhone. علامة المراجعة عند 80% أو أقل فحص أولي وليست قرار استبدال أو ضمانًا؛ حالة الخدمة تعتمد على رسالة iPhone والفحص الفني."
            }
        ];
    }


    private void BuildAuthenticityEvidence(Panel root)
    {
        root.Children.Add(Text("قرينة الاتصال الحالية", "#7DD3FC", 11, FontWeights.SemiBold));
        root.Children.Add(Text(
            $"تمت قراءة ProductType={_device.ProductType} وiOS={_device.ProductVersion} عبر Apple Lockdown، والرقم التسلسلي محجوب {_device.MaskedSerialNumber}. هذه تثبت استجابة بروتوكول الجهاز ولا تثبت أن كل مكون أصلي.",
            "#B7C9DE", 10));
        root.Children.Add(Text("نتيجة فحص الرقم التسلسلي على موقع Apple (بعد إدخاله يدويًا)", "#7DD3FC", 11, FontWeights.SemiBold));
        _coverageResult = Choice(
        [
            ("not-checked", "لم يتم الفحص"),
            ("recognized", "موقع Apple قبل الرقم وعرض بيانات/تغطية متوافقة"),
            ("expired", "موقع Apple تعرّف على الرقم لكن التغطية منتهية"),
            ("mismatch", "الموقع رفض الرقم أو ظهرت بيانات لا تطابق الجهاز")
        ]);
        root.Children.Add(_coverageResult);
        root.Children.Add(Text("الخلاصة التي قرأها الفني من سجل القطع والخدمة", "#7DD3FC", 11, FontWeights.SemiBold));
        _partsResult = Choice(
        [
            ("not-checked", "لم يتم فحص سجل القطع"),
            ("genuine", "القطع الظاهرة تحمل Genuine"),
            ("used", "يوجد جزء بحالة Used"),
            ("unknown", "يوجد Unknown أو Unverified أو Finish Repair"),
            ("not-listed", "لا يظهر سجل قطع لهذا الجهاز")
        ]);
        root.Children.Add(_partsResult);
        root.Children.Add(Notice("التفسير: الرقم الذي يقبله موقع Apple قرينة وليس إثباتًا ماديًا؛ قد تُنسخ أرقام صحيحة. وغياب Parts & Service History لا يعني أن الجهاز لم يُفتح أو لم تُبدّل قطعه. التقرير يحفظ مصدر كل قرينة ودرجة الثقة المحدودة."));
    }

    private List<ComponentDiagnostic> BuildAuthenticityEvidenceSnapshot()
    {
        var coverage = SelectedValue(_coverageResult);
        var parts = SelectedValue(_partsResult);
        if (coverage == "not-checked" && parts == "not-checked")
            return [];

        var review = coverage == "mismatch" || parts is "used" or "unknown";
        var summary = review
            ? "توجد قرينة تستدعي مراجعة فنية؛ لا تعني وحدها أن الهاتف مقلد."
            : coverage == "recognized" && parts == "genuine"
                ? "القرائن المدخلة متوافقة مع بيانات Apple الظاهرة؛ لا تمثل شهادة أصالة مطلقة ولا فحصًا ماديًا للوحة."
                : "الأدلة الحالية غير كافية لإصدار حكم نهائي.";
        return
        [
            new ComponentDiagnostic
            {
                Id = "iphone-authenticity-evidence",
                Name = "تقييم قرائن هوية وأصالة الجهاز",
                Status = review ? DiagnosticStatus.Review : DiagnosticStatus.DataRead,
                Evidence = $"Apple Lockdown ProductType={_device.ProductType}; iOS={_device.ProductVersion}; Apple coverage result={coverage}; Parts & Service History summary={parts}. {summary}",
                Description = "قرائن قرأها الفني. لا تثبت هذه المعلومات وحدها سلامة اللوحة أو الشاشة أو جميع القطع ولا تمنح حكم أصالة مطلقًا."
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
        return IPhoneAssessmentInputValidator.NormalizeNote(value);
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
