using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Opens only HTTPS OEM support/download portals. It never installs an unverified driver package.
/// </summary>
public sealed class UsbDriverCenterWindow : Window
{
    private readonly ListBox _resources = new();
    private readonly TextBlock _selectedName = new();
    private readonly TextBlock _selectedCoverage = new();
    private readonly TextBlock _selectedUrl = new();
    private readonly TextBlock _status = new();
    private OemUsbDriverResource? _selected;
    private readonly string[] _deviceHardwareIds;
    private readonly string? _deviceManufacturer;
    private readonly string? _deviceModel;

    public UsbDriverCenterWindow(
        string? manufacturer = null,
        string? model = null,
        string detectionSource = "ADB",
        IReadOnlyList<string>? hardwareIds = null)
    {
        _deviceManufacturer = manufacturer;
        _deviceModel = model;
        _deviceHardwareIds = (hardwareIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(128)
            .ToArray();
        Title = "SoftBox | OEM USB Drivers and Support Resources";
        Width = 940;
        Height = 690;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "مركز تعريفات USB وموارد الشركات الرسمية",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(Text(
            "كتالوج عالمي يضم 33 مصدرًا رسميًا للشركات ومنصات الأجهزة. ليس كل مصدر تعريف USB مستقلًا: بعضه أداة Windows أو صفحة دعم. اختر الموديل والمنطقة وإصدار Windows قبل التنزيل.",
            "#B7C9DE", 11));
        var detected = string.IsNullOrWhiteSpace(manufacturer)
            ? "لم يتم تحديد الشركة من ADB أو Windows USB."
            : $"الشركة المقروءة من {detectionSource}: {manufacturer}";
        if (!string.IsNullOrWhiteSpace(model) && model != "غير متاح")
            detected += $" • الموديل: {model}";
        else if (!string.IsNullOrWhiteSpace(manufacturer))
            detected += " • الموديل الدقيق غير متاح من بيانات USB الحالية.";
        header.Children.Add(Text(detected, "#8FC7EA", 10));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new Border
        {
            Background = Brush("#171F2C"),
            BorderBrush = Brush("#67532F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 0),
            Child = Text(
                "تنبيه توافق وأمان: هذه روابط بوابات الشركات وليست تعريفًا موحدًا لكل الموديلات. لا تثبّت إلا الحزمة التي تطابق موديل الهاتف ونسخة Windows، وتحقق من توقيع الناشر. ظهور الهاتف عبر MTP لا يمنح تصريح ADB؛ يجب تفعيل USB debugging والموافقة على الكمبيوتر من الهاتف.",
                "#EBCB9B", 10)
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });

        _resources.Background = Brush("#0C1928");
        _resources.Foreground = Brush("#EAF3FF");
        _resources.BorderBrush = Brush("#263D58");
        _resources.BorderThickness = new Thickness(1);
        _resources.SelectionChanged += (_, _) =>
        {
            if (_resources.SelectedItem is ListBoxItem { Tag: OemUsbDriverResource item })
                SelectResource(item);
        };

        foreach (var resource in OemUsbDriverCatalog.All)
        {
            var item = new ListBoxItem { Tag = resource, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(4, 2, 4, 2) };
            var card = new StackPanel();
            card.Children.Add(Text(resource.Name, "#EAF3FF", 12, true));
            card.Children.Add(Text(resource.ResourceKind, "#7DD3FC", 8, true));
            card.Children.Add(Text(resource.CoverageNote, "#8FA8C3", 9));
            item.Content = card;
            _resources.Items.Add(item);
        }
        Grid.SetColumn(_resources, 0);
        body.Children.Add(_resources);

        var details = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        details.Children.Add(Text("الشركة المحددة", "#7DD3FC", 10, true));
        _selectedName.TextWrapping = TextWrapping.Wrap;
        _selectedName.FontSize = 20;
        _selectedName.FontWeight = FontWeights.Bold;
        _selectedName.Foreground = Brush("#EAF3FF");
        _selectedName.Margin = new Thickness(0, 7, 0, 10);
        details.Children.Add(_selectedName);

        _selectedCoverage.TextWrapping = TextWrapping.Wrap;
        _selectedCoverage.FontSize = 12;
        _selectedCoverage.Foreground = Brush("#B7C9DE");
        _selectedCoverage.Margin = new Thickness(0, 0, 0, 14);
        details.Children.Add(_selectedCoverage);

        details.Children.Add(Text("الرابط الرسمي", "#7DD3FC", 10, true));
        _selectedUrl.TextWrapping = TextWrapping.Wrap;
        _selectedUrl.FontSize = 10;
        _selectedUrl.Foreground = Brush("#8FC7EA");
        _selectedUrl.Margin = new Thickness(0, 7, 0, 16);
        details.Children.Add(_selectedUrl);

        var actions = new WrapPanel();
        actions.Children.Add(ButtonControl("فتح صفحة الشركة", "#14517A", (_, _) => OpenSelected()));
        actions.Children.Add(ButtonControl("نسخ الرابط", "#20364F", (_, _) => CopySelected()));
        actions.Children.Add(ButtonControl("دليل Android OEM", "#20364F", (_, _) => OpenReference()));
        actions.Children.Add(ButtonControl("فحص توقيع حزمة تم تنزيلها", "#20364F", (_, _) => OpenSignatureVerifier()));
        actions.Children.Add(ButtonControl("مطابقة INF مع Hardware IDs", "#20364F", (_, _) => OpenInfCompatibility()));
        details.Children.Add(actions);

        _status.TextWrapping = TextWrapping.Wrap;
        _status.FontSize = 10;
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 14, 0, 0);
        details.Children.Add(_status);
        Grid.SetColumn(details, 1);
        body.Children.Add(details);

        root.Children.Add(body);
        Content = root;

        var preferred = OemUsbDriverCatalog.FindForManufacturer(manufacturer);
        var preferredItem = _resources.Items.Cast<ListBoxItem>()
            .FirstOrDefault(item => ReferenceEquals(item.Tag, preferred));
        _resources.SelectedItem = preferredItem ?? _resources.Items.Cast<ListBoxItem>().FirstOrDefault();
    }

    private void SelectResource(OemUsbDriverResource resource)
    {
        _selected = resource;
        _selectedName.Text = resource.Name;
        _selectedCoverage.Text = "نوع المصدر: " + resource.ResourceKind + "\\n" + resource.CoverageNote +
            "\\n\\nحالة توفر الحزمة: لم يتم التحقق من ملف تنزيل مباشر لهذا الموديل. افتح المصدر الرسمي وتحقق من الموديل والمنطقة وإصدار Windows والمعمارية." +
            "\\n\\nHardware IDs المقروءة من الجهاز المحدد: " + _deviceHardwareIds.Length +
            (_deviceHardwareIds.Length == 0
                ? " (غير متاحة؛ لا يمكن إثبات مطابقة الطراز حتى قراءة Hardware IDs من Windows)"
                : " (متاحة للتحقق من ملف INF)");

        _selectedUrl.Text = resource.OfficialUrl;
        _status.Text = "الحزمة لم تُنزّل بعد. نظام التشغيل الحالي: " + Environment.OSVersion.VersionString +
            " • التوافق: غير مؤكد حتى اختيار حزمة مطابقة للموديل وإصدار Windows.";
    }

    private void OpenSelected()
    {
        if (!TryGetHttpsUri(_selected?.OfficialUrl, out var uri))
        {
            _status.Text = "الرابط غير صالح أو ليس HTTPS؛ تم منع فتحه.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            _status.Text = "تم طلب فتح موقع الدعم الرسمي في المتصفح. أكمل اختيار الموديل والتنزيل من الشركة.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح صفحة الشركة: " + ex.Message;
        }
    }

    private void CopySelected()
    {
        if (!TryGetHttpsUri(_selected?.OfficialUrl, out var uri))
        {
            _status.Text = "لا يوجد رابط HTTPS صالح لنسخه.";
            return;
        }

        try
        {
            Clipboard.SetText(uri.AbsoluteUri);
            _status.Text = "تم نسخ رابط صفحة الشركة.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر نسخ الرابط: " + ex.Message;
        }
    }

    private void OpenInfCompatibility()
    {
        if (_deviceHardwareIds.Length == 0)
        {
            _status.Text = "لا توجد Hardware IDs موثقة للجهاز المحدد. افتح Universal Device Discovery واختر بطاقة PnP لهذا الهاتف حتى تمرر هويته الحقيقية.";
            return;
        }

        var window = new DriverInfCompatibilityWindow(
            _deviceHardwareIds, _deviceManufacturer, _deviceModel)
        { Owner = Owner ?? this };
        window.ShowDialog();
    }

    private void OpenSignatureVerifier()
    {
        try
        {
            var verifier = new UsbDriverSignatureVerifierWindow { Owner = Owner ?? this };
            verifier.ShowDialog();
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح مدقق التوقيع: " + ex.Message;
        }
    }

    private void OpenReference()
    {
        try
        {
            Process.Start(new ProcessStartInfo(OemUsbDriverCatalog.ReferenceUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح دليل Android OEM: " + ex.Message;
        }
    }

    private static bool TryGetHttpsUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static TextBlock Text(string value, string color, double size, bool bold = false)
        => new()
        {
            Text = value,
            Foreground = Brush(color),
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
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
