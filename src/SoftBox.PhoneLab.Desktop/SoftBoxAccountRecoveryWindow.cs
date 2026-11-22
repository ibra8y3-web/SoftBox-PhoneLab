using SoftBox.PhoneLab.Core.Security;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Requests an owner-signed support approval, then applies only the exact approved action.
/// </summary>
public sealed class SoftBoxAccountRecoveryWindow : Window
{
    private const string SupportWhatsAppUrl = "https://wa.me/201121241941?text=";
    private readonly SoftBoxAccountService _accounts;
    private readonly SoftBoxSupportAuthorizationService _authorization;
    private readonly TextBox _userName = new();
    private readonly TextBox _approval = new();
    private readonly PasswordBox _newPassword = new();
    private readonly PasswordBox _confirmPassword = new();
    private readonly TextBlock _status = new();
    private readonly Button _recoverPassword = new();
    private readonly Button _resetDatabase = new();

    public SoftBoxAccountRecoveryWindow(SoftBoxAccountService service)
    {
        _accounts = service ?? throw new ArgumentNullException(nameof(service));
        _authorization = new SoftBoxSupportAuthorizationService();
        Title = "SoftBox | استرداد الحساب بموافقة الدعم";
        Width = 620;
        Height = 790;
        MinWidth = 520;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(24), LastChildFill = true };
        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        _recoverPassword.Content = "التحقق من الموافقة واسترداد كلمة المرور";
        StyleButton(_recoverPassword, "#176044");
        _recoverPassword.Height = 42;
        _recoverPassword.Click += RecoverPassword_Click;
        footer.Children.Add(_recoverPassword);

        _resetDatabase.Content = "تصفير قاعدة الحسابات المحلية بموافقة منفصلة";
        StyleButton(_resetDatabase, "#8A3D32");
        _resetDatabase.Margin = new Thickness(0, 8, 0, 0);
        _resetDatabase.Height = 42;
        _resetDatabase.Click += ResetDatabase_Click;
        footer.Children.Add(_resetDatabase);

        _status.Text = "لا يمكن تنفيذ أي إجراء برقم عشوائي. يجب استيراد موافقة صحيحة موقعة ومخصصة لهذا التثبيت والطلب والإجراء.";
        _status.Foreground = Brush("#F0C9A5");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 12, 0, 0);
        footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var form = new StackPanel();
        form.Children.Add(new TextBlock
        {
            Text = "استرداد حساب SoftBox",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        form.Children.Add(new TextBlock
        {
            Text = "اطلب موافقة من الدعم عبر WhatsApp. سيُصدر المالك موافقة رقمية لا تعمل إلا على هذا التثبيت ولإجراء واحد ولمرة واحدة.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = Brush("#9CB3CC"),
            Margin = new Thickness(0, 8, 0, 8)
        });

        AddTextField(form, "اسم المستخدم (لاسترداد كلمة المرور)", _userName, "اسم المستخدم المحلي");
        var requestButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        var passwordRequest = new Button { Content = "طلب استرداد كلمة المرور عبر WhatsApp" };
        StyleButton(passwordRequest, "#14517A");
        passwordRequest.Click += RequestPasswordRecovery_Click;
        requestButtons.Children.Add(passwordRequest);
        var resetRequest = new Button { Content = "طلب تصفير الحسابات" };
        StyleButton(resetRequest, "#713B35");
        resetRequest.Margin = new Thickness(8, 0, 0, 0);
        resetRequest.Click += RequestDatabaseReset_Click;
        requestButtons.Children.Add(resetRequest);
        form.Children.Add(requestButtons);

        AddTextField(form, "ملف الموافقة الموقعة (الصق JSON الذي أرسله الدعم)", _approval, "الصق محتوى ملف الموافقة هنا");
        _approval.Height = 150;
        _approval.AcceptsReturn = true;
        _approval.TextWrapping = TextWrapping.Wrap;
        _approval.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _approval.FontFamily = new FontFamily("Consolas");

        AddPasswordField(form, "كلمة المرور الجديدة", _newPassword);
        AddPasswordField(form, "تأكيد كلمة المرور الجديدة", _confirmPassword);
        form.Children.Add(new TextBlock
        {
            Text = $"يُطلب إدخال كلمة المرور فقط عند استرداد حساب محدد. الحد الأدنى {SoftBoxAccountService.MinimumPasswordLength} حرفًا. تصفير قاعدة الحسابات إجراء منفصل يمسح حسابات المستخدمين المحلية ويُبقي أدوات SoftBox والإعدادات وهوية التثبيت وسجل منع إعادة الاستخدام.",
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#9CB3CC"),
            Margin = new Thickness(0, 12, 0, 0)
        });
        root.Children.Add(form);
        return root;
    }

    private void RequestPasswordRecovery_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var request = _authorization.CreatePasswordRecoveryRequest(_userName.Text);
            OpenWhatsAppRequest(request, "طلب استرداد كلمة مرور حساب SoftBox");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void RequestDatabaseReset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = MessageBox.Show(this,
                "طلب تصفير قاعدة الحسابات يختلف عن استرداد كلمة المرور، وسيحذف حسابات SoftBox المحلية عند تطبيق موافقة المالك. هل تريد إنشاء هذا الطلب؟",
                "تأكيد طلب تصفير الحسابات", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
            var request = _authorization.CreateDatabaseResetRequest();
            OpenWhatsAppRequest(request, "طلب تصفير قاعدة حسابات SoftBox المحلية");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void OpenWhatsAppRequest(string requestJson, string title)
    {
        var message = title + Environment.NewLine +
            "يرجى مراجعة طلب JSON وإرساله كما هو. لا يحتوي الطلب على كلمة المرور:" +
            Environment.NewLine + requestJson;
        try { Clipboard.SetText(requestJson); } catch { }
        Process.Start(new ProcessStartInfo(SupportWhatsAppUrl + Uri.EscapeDataString(message)) { UseShellExecute = true });
        _status.Text = "تم إنشاء طلب خاص بهذا التثبيت ونسخه إلى الحافظة. افتح WhatsApp واضغط إرسال. لا ترسل كلمة المرور.";
        _status.Foreground = Brush("#86EFAC");
    }

    private async void RecoverPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!_recoverPassword.IsEnabled) return;
        if (!string.Equals(_newPassword.Password, _confirmPassword.Password, StringComparison.Ordinal))
        { SetError("كلمتا المرور الجديدتان غير متطابقتين."); return; }

        SetBusy(true);
        try
        {
            var user = await _authorization.RecoverPasswordAsync(
                _accounts, _approval.Text, _userName.Text, _newPassword.Password);
            MessageBox.Show(this,
                $"تم استرداد الحساب {user.UserName}. استخدم كلمة المرور الجديدة لتسجيل الدخول.",
                "SoftBox | تم الاسترداد", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            _newPassword.Clear();
            _confirmPassword.Clear();
        }
        finally { if (IsVisible) SetBusy(false); }
    }

    private async void ResetDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (!_resetDatabase.IsEnabled) return;
        var confirm = MessageBox.Show(this,
            "سيتم حذف قاعدة حسابات المستخدمين المحلية بالكامل. ستبقى أدوات SoftBox والإعدادات وملفات الأجهزة وهوية التثبيت وسجل منع إعادة الاستخدام. بعد العملية أغلق SoftBox وافتحه مجددًا لإنشاء مدير جديد. هل توافق؟",
            "تأكيد نهائي لتصفير قاعدة الحسابات", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
        {
            await _authorization.ResetLocalAccountDatabaseAsync(_accounts, _approval.Text);
            MessageBox.Show(this,
                "تم تصفير قاعدة الحسابات المحلية. أغلق SoftBox وافتحه مجددًا لإعداد حساب المدير الأول. الموافقة المستهلكة لا يمكن استخدامها مرة أخرى.",
                "SoftBox | اكتمل التصفير", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown(0);
        }
        catch (Exception ex) { SetError(ex.Message); }
        finally { if (IsVisible) SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _recoverPassword.IsEnabled = !busy;
        _resetDatabase.IsEnabled = !busy;
    }

    private static void AddTextField(Panel panel, string label, TextBox field, string hint)
    {
        AddLabel(panel, label);
        field.MinHeight = 38;
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
