using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Hands off to the built-in Windows Wireless Display optional feature.
/// This is an OS-level Miracast receiver, not an embedded SoftBox screen stream.
/// </summary>
public sealed class MiracastSetupWindow : Window
{
    private const string MicrosoftGuide =
        "https://support.microsoft.com/en-us/windows/hardware/display-graphics/screen-mirroring-and-projecting-to-your-pc-or-wireless-display";

    private readonly TextBlock _status = new();

    public MiracastSetupWindow()
    {
        Title = "SoftBox | Windows Wireless Display / Miracast";
        Width = 760;
        Height = 580;
        MinWidth = 620;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "مسار Miracast الاحتياطي عبر Windows",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        root.Children.Add(Text(
            "قد يفيد هذا المسار بعض هواتف Android وHuawei وOPPO وغيرها التي تدعم Miracast، حتى عندما لا يتوفر ADB. يتطلب دعم Miracast على الهاتف وعلى محول Wi‑Fi/تعريفات الكمبيوتر.",
            "#B7C9DE", 11));

        root.Children.Add(Warning(
            "هذا مستقبل Windows الأصلي وليس بثًا مدمجًا داخل لوحة SoftBox. لا يمنح SoftBox تحكمًا باللمس، ولا يثبت أن الهاتف يدعم Miracast. إذا كان الجهاز لا يدعم Miracast فلن يظهر في قائمة الإرسال."));

        root.Children.Add(Text("خطوات الإعداد على Windows 11", "#7DD3FC", 12));
        root.Children.Add(Text(
            "1) افتح ميزات Windows الاختيارية وثبّت Wireless Display إذا لم تكن مثبتة.\n" +
            "2) افتح Settings > System > Projecting to this PC واضبط السماح بالاكتشاف حسب سياسة جهازك.\n" +
            "3) ابحث من قائمة Start عن تطبيق Wireless Display وشغّله على الكمبيوتر.\n" +
            "4) على الهاتف افتح Cast / Screen Mirroring / Wireless Projection واختر هذا الكمبيوتر.\n" +
            "5) عند الانتهاء أوقف الاتصال من الهاتف أو من تطبيق Wireless Display.",
            "#DCEBFA", 10));
        root.Children.Add(Text("Windows 10", "#7DD3FC", 12));
        root.Children.Add(Text(
            "ثبّت ميزة Wireless Display الاختيارية، ثم افتح تطبيق Connect من قائمة Start لاستقبال الإسقاط إذا كان الكمبيوتر يدعم Miracast.",
            "#DCEBFA", 10));

        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 8) };
        buttons.Children.Add(Button("فتح إعداد Projecting to this PC", "#14517A", (_, _) =>
            OpenUri("ms-settings:project")));
        buttons.Children.Add(Button("فتح الميزات الاختيارية", "#20364F", (_, _) =>
            OpenUri("ms-settings:optionalfeatures")));
        buttons.Children.Add(Button("دليل Microsoft الرسمي", "#20364F", (_, _) =>
            OpenUri(MicrosoftGuide)));
        root.Children.Add(buttons);

        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 10, 0, 0);
        _status.Text = "لم يغيّر SoftBox ميزات Windows أو إعدادات الشبكة. أكمل الإعداد يدويًا ووافق على الاتصال من الهاتف.";
        root.Children.Add(_status);
        return root;
    }

    private void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            _status.Text = "تم طلب فتح صفحة إعداد Windows أو دليل Microsoft. لم يتم تثبيت أي ميزة أو بدء استقبال Miracast تلقائيًا.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح صفحة الإعداد: " + ex.Message;
        }
    }

    private static Border Warning(string message)
        => new()
        {
            Background = Brush("#30251B"),
            BorderBrush = Brush("#8B6238"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11),
            Margin = new Thickness(0, 10, 0, 12),
            Child = Text(message, "#FFE0B7", 10)
        };

    private static Button Button(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(11, 8, 11, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Background = Brush(color),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096")
        };
        button.Click += handler;
        return button;
    }

    private static TextBlock Text(string value, string color, double size)
        => new() { Text = value, Foreground = Brush(color), FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) };

    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value)!);
}
