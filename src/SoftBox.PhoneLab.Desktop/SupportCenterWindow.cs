using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Local SoftBox help and owner-operated WhatsApp support entry point.
/// </summary>
public sealed class SupportCenterWindow : Window
{
    private const string WhatsAppNumber = "201121241941";
    private const string WhatsAppUrl = "https://wa.me/" + WhatsAppNumber + "?text=";

    public SupportCenterWindow()
    {
        Title = "SoftBox | الدعم الفني";
        Width = 680;
        Height = 520;
        MinWidth = 540;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(24), LastChildFill = true };
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        var contact = new Button { Content = "فتح محادثة دعم SoftBox عبر WhatsApp" };
        contact.Padding = new Thickness(14, 10, 14, 10);
        contact.Background = Brush("#176044");
        contact.Foreground = Brushes.White;
        contact.BorderBrush = Brush("#3E7096");
        contact.FontWeight = FontWeights.SemiBold;
        contact.Click += Contact_Click;
        footer.Children.Add(contact);
        var bundle = new Button { Content = "إنشاء حزمة تشخيص للدعم" };
        bundle.Padding = new Thickness(14, 10, 14, 10);
        bundle.Background = Brush("#14517A");
        bundle.Foreground = Brushes.White;
        bundle.BorderBrush = Brush("#3E7096");
        bundle.Margin = new Thickness(0, 8, 0, 0);
        bundle.Click += (_, _) => new SupportBundleWindow { Owner = this }.ShowEmbedded();
        footer.Children.Add(bundle);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "مركز دعم SoftBox",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        body.Children.Add(new TextBlock
        {
            Text = "WhatsApp: 01121241941",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#86EFAC"),
            Margin = new Thickness(0, 10, 0, 0)
        });
        body.Children.Add(new TextBlock
        {
            Text = "لاسترداد كلمة المرور، افتح «نسيت كلمة المرور؟» من شاشة تسجيل الدخول، وأنشئ طلب دعم موقّعًا بالهوية الخاصة بهذا التثبيت. بعد مراجعة الطلب يرسل المالك موافقة رقمية محددة للمستخدم ولمرة واحدة.",
            FontSize = 12,
            Foreground = Brush("#C6D6E8"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 18, 0, 0)
        });
        body.Children.Add(new TextBlock
        {
            Text = "قواعد الأمان: لا ترسل كلمة المرور أو ملفات قاعدة الحسابات. موافقة استرداد كلمة المرور لا تصلح لتصفير قاعدة الحسابات؛ لكل إجراء موافقة مستقلة. التوقيع مرتبط بمعرّف التثبيت والطلب والمدة المحددة، وتُحفظ هوية التثبيت وسجلات الاستخدام خارج قاعدة الحسابات.",
            FontSize = 11,
            Foreground = Brush("#F0C9A5"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0)
        });
        root.Children.Add(body);
        return root;
    }

    private void Contact_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var message = Uri.EscapeDataString(
                "مرحبًا دعم SoftBox، أحتاج مساعدة فنية. أرجو إرشادي إلى الخطوات المناسبة. لن أرسل كلمة المرور أو ملف رموز الاسترداد.");
            Process.Start(new ProcessStartInfo(WhatsAppUrl + message) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "تعذر فتح WhatsApp: " + ex.Message,
                "SoftBox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
