using SoftBox.PhoneLab.Core.Security;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Password recovery using an administrator-issued, one-time code for this installation.
/// No account is unlocked by a universal password or support contact alone.
/// </summary>
public sealed class SoftBoxAccountRecoveryWindow : Window
{
    private const string SupportWhatsAppUrl = "https://wa.me/201121241941?text=";
    private readonly SoftBoxAccountService _service;
    private readonly TextBox _userName = new();
    private readonly TextBox _recoveryCode = new();
    private readonly PasswordBox _newPassword = new();
    private readonly PasswordBox _confirmPassword = new();
    private readonly TextBlock _status = new();
    private readonly Button _submit = new();

    public SoftBoxAccountRecoveryWindow(SoftBoxAccountService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Title = "SoftBox | استرداد الحساب";
        Width = 560;
        Height = 700;
        MinWidth = 480;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(24), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock
        {
            Text = "استرداد حساب SoftBox",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "أدخل اسم المستخدم ورمز دعم أحادي الاستخدام صادرًا لهذا التثبيت، ثم اختر كلمة مرور جديدة.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brush("#9CB3CC"),
            Margin = new Thickness(0, 8, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        _submit.Content = "التحقق من الرمز وتغيير كلمة المرور";
        StyleButton(_submit, "#176044");
        _submit.Height = 44;
        _submit.Click += Submit_Click;
        footer.Children.Add(_submit);

        var contact = new Button { Content = "التواصل مع دعم SoftBox عبر WhatsApp" };
        StyleButton(contact, "#14517A");
        contact.Margin = new Thickness(0, 8, 0, 0);
        contact.Click += ContactSupport_Click;
        footer.Children.Add(contact);

        _status.Text = "رمز الدعم لا يُستخدم إلا مرة واحدة، ولا يعمل على تثبيت SoftBox آخر.";
        _status.Foreground = Brush("#F0C9A5");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 12, 0, 0);
        footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var form = new StackPanel();
        AddTextField(form, "اسم المستخدم", _userName, "مثال: sbx-12345678");
        AddTextField(form, "رمز الاسترداد الذي أرسله الدعم", _recoveryCode, "الصق الرمز المكوّن من 32 حرفًا/رقمًا");
        AddPasswordField(form, "كلمة المرور الجديدة", _newPassword);
        AddPasswordField(form, "تأكيد كلمة المرور الجديدة", _confirmPassword);
        form.Children.Add(new TextBlock
        {
            Text = $"كلمة المرور الجديدة يجب أن تكون {SoftBoxAccountService.MinimumPasswordLength} حرفًا على الأقل. لا ترسل كلمة المرور للدعم؛ الرمز وحده يكفي لإتمام الاسترداد.",
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#9CB3CC"),
            Margin = new Thickness(0, 12, 0, 0)
        });
        root.Children.Add(form);
        return root;
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (!_submit.IsEnabled)
            return;
        if (!string.Equals(_newPassword.Password, _confirmPassword.Password, StringComparison.Ordinal))
        {
            SetError("كلمتا المرور الجديدتان غير متطابقتين.");
            return;
        }

        _submit.IsEnabled = false;
        try
        {
            var user = await _service.RecoverPasswordWithSupportCodeAsync(
                _userName.Text, _recoveryCode.Text, _newPassword.Password);
            MessageBox.Show(this,
                $"تم استرداد الحساب {user.UserName} وتغيير كلمة المرور. استخدم كلمة المرور الجديدة لتسجيل الدخول.",
                "SoftBox | تم الاسترداد", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            _newPassword.Clear();
            _confirmPassword.Clear();
            _newPassword.Focus();
        }
        finally
        {
            if (IsVisible)
                _submit.IsEnabled = true;
        }
    }

    private void ContactSupport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var user = Uri.EscapeDataString(_userName.Text.Trim());
            var message = Uri.EscapeDataString(
                "مرحبًا دعم SoftBox، أحتاج مساعدة في استرداد حساب SoftBox المحلي. اسم المستخدم: " +
                (string.IsNullOrWhiteSpace(_userName.Text) ? "سأرسله بعد التواصل" : _userName.Text.Trim()) +
                ". أرجو التحقق من ملكية الحساب وإصدار رمز استرداد أحادي الاستخدام لهذا التثبيت. لن أرسل كلمة المرور.");
            Process.Start(new ProcessStartInfo(SupportWhatsAppUrl + message) { UseShellExecute = true });
            _status.Text = "تم فتح WhatsApp. لا ترسل كلمة المرور أو ملف الرموز الكامل؛ اطلب رمزًا واحدًا بعد التحقق من ملكية الحساب.";
            _status.Foreground = Brush("#86EFAC");
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح WhatsApp: " + ex.Message;
            _status.Foreground = Brush("#FCA5A5");
        }
    }

    private static void AddTextField(Panel panel, string label, TextBox field, string hint)
    {
        AddLabel(panel, label);
        field.Height = 38;
        field.Padding = new Thickness(10, 7, 10, 7);
        field.Background = Brush("#0A1B2D");
        field.Foreground = Brush("#EAF3FF");
        field.BorderBrush = Brush("#35516F");
        field.ToolTip = hint;
        panel.Children.Add(field);
    }

    private static void AddPasswordField(Panel panel, string label, PasswordBox field)
    {
        AddLabel(panel, label);
        field.Height = 38;
        field.Padding = new Thickness(10, 7, 10, 7);
        field.Background = Brush("#0A1B2D");
        field.Foreground = Brush("#EAF3FF");
        field.BorderBrush = Brush("#35516F");
        panel.Children.Add(field);
    }

    private static void AddLabel(Panel panel, string text)
        => panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = Brush("#AFC6DE"),
            Margin = new Thickness(0, 10, 0, 5)
        });

    private static void StyleButton(Button button, string color)
    {
        button.Padding = new Thickness(12, 8, 12, 8);
        button.Background = Brush(color);
        button.Foreground = Brushes.White;
        button.BorderBrush = Brush("#3E7096");
        button.FontWeight = FontWeights.SemiBold;
    }

    private void SetError(string message)
    {
        _status.Text = message;
        _status.Foreground = Brush("#FCA5A5");
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
