using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Core.Security;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Evidence-limited, read-only diagnostics and official recovery hand-offs.
/// It never resets, unlocks, removes accounts, changes MDM policy or modifies modem state.
/// </summary>
public sealed class DeviceAccessRecoveryWindow : Window
{
    private readonly AdbClient? _adb;
    private readonly string? _serial;
    private readonly string _deviceLabel;
    private readonly Button _scanButton;
    private readonly TextBlock _scanStatus = MakeText("الفحص لم يبدأ.", "#9FC7E8");
    private readonly TextBlock _keyguardResult = MakeText("غير مفحوص", "#DCEBFA");
    private readonly TextBlock _managementResult = MakeText("غير مفحوص", "#DCEBFA");
    private readonly TextBlock _simResult = MakeText("غير مفحوص", "#DCEBFA");
    private readonly TextBlock _networkResult = MakeText(
        "حالة فتح الشبكة لا يمكن استنتاجها من حالة SIM أو اسم المشغل. اطلب نتيجة IMEI من شركة الاتصالات الأصلية.",
        "#F0C9A5");
    private readonly TextBlock _accountResult = MakeText(
        "حالة Google FRP وSamsung Account وXiaomi Activation Lock لا تُعرض بواجهة ADB عامة موثوقة؛ استخدم مسار استعادة الحساب/الشركة الرسمي.",
        "#F0C9A5");

    private static readonly (string Title, string Description, string Url)[] RecoveryResources =
    [
        ("Google Account Recovery",
            "استعادة حساب Google المرتبط بالهاتف. لا تعتبر إعادة ضبط المصنع طريقة لإزالة FRP.",
            "https://accounts.google.com/signin/recovery"),
        ("Google Device Protection / FRP",
            "شرح Google الرسمي لحماية الجهاز ومتطلبات إثبات الملكية بعد إعادة الضبط.",
            "https://support.google.com/android/answer/9459346?hl=en"),
        ("Google: Can't unlock Android device",
            "إرشادات Google الرسمية عند نسيان PIN أو Pattern أو Password؛ خيارات المسح تؤدي إلى حذف البيانات وقد يبقى FRP.",
            "https://support.google.com/android/answer/7663172?hl=en"),
        ("Samsung Account",
            "بوابة Samsung الرسمية لتسجيل الدخول واستعادة الحساب.",
            "https://account.samsung.com/"),
        ("Samsung Authorized Service Centers",
            "استخدم مركز خدمة Samsung المعتمد في بلد شراء الجهاز مع إثبات الملكية.",
            "https://www.samsung.com/eg/support/service-center/"),
        ("Xiaomi Account Recovery",
            "إعادة تعيين كلمة مرور Mi Account أو تحديث معلومات الاسترداد.",
            "https://account.xiaomi.com/helpcenter/faq/en_US/02.faqs/03.password/faq-1"),
        ("Xiaomi Locked-device Appeal",
            "إرسال طلب مراجعة رسمي إلى Xiaomi عند قفل الجهاز المرتبط بحساب Mi.",
            "https://account.xiaomi.com/helpcenter/faq/en_US/01.guide/11.appeal/faq-6"),
        ("Android Enterprise / MDM",
            "مسارات الإدارة المؤسسية وFRP المؤسسي؛ إزالة الإدارة يجب أن تتم من مسؤول المؤسسة أو مزود EMM.",
            "https://support.google.com/work/android/answer/14549362?hl=en")
    ];

    public DeviceAccessRecoveryWindow(AdbClient? adb, string? serial, string? manufacturer, string? model)
    {
        _adb = adb;
        _serial = AdbClient.IsSafeSerial(serial ?? string.Empty) ? serial : null;
        _deviceLabel = string.Join(" ", new[] { manufacturer, model }.Where(value => !string.IsNullOrWhiteSpace(value)));
        _scanButton = new Button
        {
            Content = "فحص مؤشرات الجهاز (قراءة فقط)",
            Padding = new Thickness(16, 9, 16, 9),
            Background = Brush("#14517A"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold,
            IsEnabled = _adb is not null && _serial is not null
        };
        _scanButton.Click += ScanButton_Click;

        Title = "SoftBox | Device Access & Official Recovery";
        Width = 980;
        Height = 820;
        MinWidth = 800;
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "فحص حماية الجهاز والاستعادة الرسمية",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(MakeText(
            "تشخيص Android عام عبر ADB المصرح فقط، مع توجيه رسمي للحسابات والشركات وشركات الاتصالات. لا توجد عمليات تجاوز أو إزالة أقفال.",
            "#B7C9DE"));
        header.Children.Add(MakeText(
            "الجهاز المحدد: " + (string.IsNullOrWhiteSpace(_deviceLabel) ? "لم يتم تحديد موديل" : _deviceLabel) +
            (_serial is null ? " • لا يوجد اتصال ADB مصرح به" : " • Serial: " + MaskSerial(_serial)),
            "#8199B5"));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var scanPanel = new Border
        {
            Background = Brush("#10243B"),
            BorderBrush = Brush("#284C6A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var scanLayout = new StackPanel();
        scanLayout.Children.Add(_scanButton);
        scanLayout.Children.Add(_scanStatus);
        scanPanel.Child = scanLayout;
        DockPanel.SetDock(scanPanel, Dock.Top);
        root.Children.Add(scanPanel);

        var disclaimer = new Border
        {
            Background = Brush("#30251B"),
            BorderBrush = Brush("#8B6238"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = MakeText(
                "تنبيه مهم: لا يمكن فحص FRP أو Mi Activation Lock أو Samsung Account أو حالة فتح الشبكة بشكل موثوق من ADB عام. إعادة ضبط المصنع أو تفليش النظام قد يمسح البيانات ولا يزيل بالضرورة حماية الحساب. غياب مؤشر في التقرير لا يثبت أن الجهاز غير مقفول أو غير مُدار.",
                "#FFE0B7")
        };
        DockPanel.SetDock(disclaimer, Dock.Top);
        root.Children.Add(disclaimer);

        var content = new StackPanel();
        content.Children.Add(CreateResultCard("حالة شاشة القفل لحظة الفحص", _keyguardResult));
        content.Children.Add(CreateResultCard("مؤشرات Device Owner / Profile Owner / MDM", _managementResult));
        content.Children.Add(CreateResultCard("حالة SIM التي يعرضها Android", _simResult));
        content.Children.Add(CreateResultCard("قفل SIM / Network", _networkResult));
        content.Children.Add(CreateResultCard("Google FRP / Samsung Account / Xiaomi Activation Lock", _accountResult));

        var resources = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        resources.Children.Add(new TextBlock
        {
            Text = "مسارات الاستعادة الرسمية",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC"),
            Margin = new Thickness(2, 0, 2, 8)
        });
        foreach (var resource in RecoveryResources)
            resources.Children.Add(CreateResourceCard(resource.Title, resource.Description, resource.Url));
        content.Children.Add(resources);

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        root.Children.Add(scroll);
        return root;
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_adb is null || _serial is null)
        {
            _scanStatus.Text = "لا يوجد جهاز ADB مصرح به. يمكن استخدام روابط الاستعادة الرسمية دون فحص الجهاز.";
            return;
        }

        _scanButton.IsEnabled = false;
        _scanStatus.Text = "جارٍ تنفيذ قراءات محددة فقط: dumpsys device_policy وdumpsys window وخصائص SIM...";
        var warnings = new List<string>();

        try
        {
            var state = await _adb.RunForDeviceAsync(
                _serial, ["get-state"], CancellationToken.None, TimeSpan.FromSeconds(8));
            if (!state.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("الجهاز المحدد لم يعد في حالة ADB authorized.");

            var policy = await ReadOptionalAsync(["shell", "dumpsys", "device_policy"], warnings);
            var window = await ReadOptionalAsync(["shell", "dumpsys", "window"], warnings);
            var simState = await ReadOptionalAsync(["shell", "getprop", "gsm.sim.state"], warnings);
            var operatorName = await ReadOptionalAsync(["shell", "getprop", "gsm.operator.alpha"], warnings);

            var report = DeviceAccessDiagnosticsParser.Parse(policy, window, simState, operatorName);
            _keyguardResult.Text = report.KeyguardVisibility switch
            {
                KeyguardVisibility.Visible =>
                    "أظهر Android أن شاشة القفل ظاهرة وقت الفحص. هذا لا يكشف PIN أو Pattern ولا يسمح بتغييرهما.",
                KeyguardVisibility.NotVisible =>
                    "أظهر Android أن شاشة القفل غير ظاهرة وقت الفحص فقط. هذا لا يثبت عدم وجود قفل مضبوط.",
                _ => "لم تُعرض إشارة Keyguard معروفة أو كانت الإشارات متعارضة؛ النتيجة غير محددة."
            };

            _managementResult.Text = !report.PolicyOutputReadable
                ? "تعذرت قراءة Device Policy Manager أو أن الشركة/إصدار Android لا يعرض الصيغة المتوقعة."
                : report.HasManagementOwner
                    ? "عُثر على مؤشر Device Owner أو Profile Owner ضمن تقرير إدارة الجهاز. إزالة الإدارة تحتاج مسؤول المؤسسة/مزود EMM."
                    : report.HasActiveAdmin
                        ? "عُثر على مؤشر Active Admin، لكن لم يظهر Device Owner/Profile Owner واضح. هذا لا ينفي وجود MDM أو سياسة أخرى."
                        : "لم تظهر مؤشرات Device Owner/Profile Owner/Active Admin المعروفة في المخرجات المقروءة. هذا ليس إثباتًا لغياب MDM.";

            _simResult.Text =
                "SIM state: " + (string.IsNullOrWhiteSpace(report.SimStateProperty) ? "غير متاح" : report.SimStateProperty) +
                " • Network operator: " + (string.IsNullOrWhiteSpace(report.NetworkOperatorProperty)
                    ? "غير متاح" : report.NetworkOperatorProperty) +
                ". هذه خصائص معلوماتية فقط وليست حكمًا على قفل الشبكة.";

            _networkResult.Text =
                "حالة فتح الشبكة: غير محددة. لا يستنتجها SoftBox من وجود SIM أو ظهور اسم المشغل. يلزم استعلام IMEI لدى شركة الاتصالات الأصلية والتحقق من أهلية الجهاز.";

            _accountResult.Text =
                "حالة FRP أو Samsung Account أو Mi Activation Lock: غير متاحة عبر واجهة ADB عامة موثوقة. لا يُسجل SoftBox أن القفل موجود أو غير موجود دون رد رسمي من الجهة المالكة للحماية.";

            _scanStatus.Text = warnings.Count == 0
                ? "اكتمل الفحص. النتائج قراءات محدودة وليست شهادة بأن الهاتف خالٍ من الأقفال."
                : "اكتمل الفحص جزئيًا. تعذر تنفيذ: " + string.Join("، ", warnings.Distinct(StringComparer.Ordinal));
        }
        catch (Exception ex)
        {
            _scanStatus.Text = "تعذر إكمال الفحص: " + ex.Message;
        }
        finally
        {
            _scanButton.IsEnabled = true;
        }
    }

    private async Task<string?> ReadOptionalAsync(string[] arguments, List<string> warnings)
    {
        try
        {
            return await _adb!.RunForDeviceAsync(
                _serial!, arguments, CancellationToken.None, TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            warnings.Add(arguments.Length >= 3 ? arguments[^1] : "قراءة فرعية");
            System.Diagnostics.Debug.WriteLine("SoftBox access diagnostic read failed: " + ex.Message);
            return null;
        }
    }

    private static Border CreateResultCard(string title, TextBlock value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#7DD3FC"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        value.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(value);
        return new Border
        {
            Background = Brush("#0F1E32"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel
        };
    }

    private static Border CreateResourceCard(string title, string description, string url)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#EAF3FF")
        });
        panel.Children.Add(MakeText(description, "#AFC2D7"));
        var button = new Button
        {
            Content = "فتح المصدر الرسمي",
            Tag = url,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 7, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush("#14517A"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096")
        };
        button.Click += OpenResource_Click;
        panel.Children.Add(button);
        return new Border
        {
            Background = Brush("#0F1E32"),
            BorderBrush = Brush("#263D58"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 7),
            Child = panel
        };
    }

    private static void OpenResource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("تعذر فتح الرابط الرسمي: " + ex.Message, "SoftBox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static TextBlock MakeText(string value, string color)
        => new()
        {
            Text = value,
            Foreground = Brush(color),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        };

    private static string MaskSerial(string serial)
        => serial.Length <= 4 ? "••••" : "••••" + serial[^4..];

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color)!);
}
