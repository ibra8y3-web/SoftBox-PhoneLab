using SoftBox.PhoneLab.Core.Security;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// First-run administrator bootstrap and mandatory local account sign-in.
/// No shared/default password is ever created.
/// </summary>
public sealed class SoftBoxAuthenticationWindow : Window
{
    private readonly SoftBoxAccountService _service;
    private readonly bool _initialSetup;
    private readonly TextBox _userName = new();
    private readonly TextBox _displayName = new();
    private readonly PasswordBox _password = new();
    private readonly PasswordBox _confirmPassword = new();
    private readonly TextBlock _status = new();
    private readonly Button _submit;
    private readonly Button _generateCredentials = new();
    private readonly Button _copyCredentials = new();

    public AuthenticatedSoftBoxUser? AuthenticatedUser { get; private set; }

    public SoftBoxAuthenticationWindow(SoftBoxAccountService service, bool initialSetup)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _initialSetup = initialSetup;

        Title = initialSetup ? "SoftBox | إعداد المدير الأول" : "SoftBox | تسجيل الدخول";
        Width = 500;
        Height = initialSetup ? 640 : 520;
        MinWidth = 440;
        MinHeight = initialSetup ? 600 : 480;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _submit = ButtonControl(initialSetup ? "إنشاء حساب المدير والدخول" : "تسجيل الدخول إلى SoftBox", "#176044", Submit_Click);
        Content = BuildContent();
        Loaded += (_, _) => _userName.Focus();
    }

    private UIElement BuildContent()
    {
        var root = new Grid { Margin = new Thickness(28) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock
        {
            Text = "SoftBox",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = _initialSetup ? "إعداد الحساب الإداري الأول" : "حساب المستخدم المحلي",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 4)
        });
        header.Children.Add(new TextBlock
        {
            Text = _initialSetup
                ? "لا يوجد حساب في هذا التثبيت بعد. أنشئ حساب المدير بنفسك؛ لا توجد كلمة مرور افتراضية."
                : "أدخل بيانات حساب SoftBox المحلي للمتابعة. الحسابات المعطلة أو المقفلة مؤقتًا لن تتمكن من الدخول.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#9CB3CC"),
            FontSize = 11
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var form = new StackPanel();
        if (_initialSetup)
            AddField(form, "اسم المدير الظاهر", _displayName, "مثال: مدير مركز الصيانة");
        AddField(form, "اسم المستخدم", _userName, _initialSetup ? "admin أو اسم عشوائي" : "اسم المستخدم");
        _userName.TextChanged += (_, _) => _copyCredentials.IsEnabled = !_initialSetup || !string.IsNullOrWhiteSpace(_userName.Text) || !string.IsNullOrEmpty(_password.Password);
        if (_initialSetup)
        {
            var credentialActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            _generateCredentials.Content = "توليد بيانات عشوائية لهذا التثبيت";
            _generateCredentials.Padding = new Thickness(10, 6, 10, 6);
            _generateCredentials.Background = Brush("#14517A");
            _generateCredentials.Foreground = Brushes.White;
            _generateCredentials.Click += GenerateCredentials_Click;
            _copyCredentials.Content = "نسخ بيانات الدخول";
            _copyCredentials.Padding = new Thickness(10, 6, 10, 6);
            _copyCredentials.Margin = new Thickness(8, 0, 0, 0);
            _copyCredentials.Background = Brush("#24364B");
            _copyCredentials.Foreground = Brushes.White;
            _copyCredentials.IsEnabled = false;
            _copyCredentials.Click += CopyCredentials_Click;
            credentialActions.Children.Add(_generateCredentials);
            credentialActions.Children.Add(_copyCredentials);
            form.Children.Add(credentialActions);
        }
        _password.PasswordChanged += (_, _) => _copyCredentials.IsEnabled = !_initialSetup || !string.IsNullOrWhiteSpace(_userName.Text) || !string.IsNullOrEmpty(_password.Password);
        AddPasswordField(form, "كلمة المرور", _password);
        if (_initialSetup)
        {
            AddPasswordField(form, "تأكيد كلمة المرور", _confirmPassword);
            form.Children.Add(new TextBlock
            {
                Text = $"الحد الأدنى {SoftBoxAccountService.MinimumPasswordLength} حرفًا. استخدم عبارة مرور طويلة وفريدة.",
                Foreground = Brush("#F0C9A5"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }

        _status.Text = _initialSetup ? "سيتم حفظ الحساب محليًا في ملف حماية SoftBox." : "سجّل الدخول لفتح مساحة العمل.";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 16, 0, 0);
        form.Children.Add(_status);
        Grid.SetRow(form, 1);
        root.Children.Add(form);

        var footer = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        _submit.HorizontalAlignment = HorizontalAlignment.Stretch;
        _submit.Height = 44;
        _submit.IsDefault = true;
        footer.Children.Add(_submit);

        if (!_initialSetup)
        {
            var recovery = ButtonControl("نسيت كلمة المرور؟ استرداد برمز الدعم", "#14517A", (_, _) =>
            {
                var window = new SoftBoxAccountRecoveryWindow(_service) { Owner = this };
                window.ShowDialog();
            });
            recovery.Margin = new Thickness(0, 8, 0, 0);
            recovery.HorizontalAlignment = HorizontalAlignment.Stretch;
            footer.Children.Add(recovery);
        }

        var exit = ButtonControl("خروج", "#24364B", (_, _) => { DialogResult = false; Close(); });
        exit.Margin = new Thickness(0, 8, 0, 0);
        exit.HorizontalAlignment = HorizontalAlignment.Stretch;
        exit.IsCancel = true;
        footer.Children.Add(exit);
        footer.Children.Add(new TextBlock
        {
            Text = "SoftBox • Local authentication • Passwords are stored as PBKDF2 verifiers, never as readable text.",
            Foreground = Brush("#657F9C"),
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        });
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private static void AddField(Panel panel, string label, TextBox field, string placeholder)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush("#AFC6DE"),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 5)
        });
        field.Height = 38;
        field.Padding = new Thickness(10, 7, 10, 7);
        field.Background = Brush("#0A1B2D");
        field.Foreground = Brush("#EAF3FF");
        field.BorderBrush = Brush("#35516F");
        field.ToolTip = placeholder;
        panel.Children.Add(field);
    }

    private static void AddPasswordField(Panel panel, string label, PasswordBox field)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush("#AFC6DE"),
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 5)
        });
        field.Height = 38;
        field.Padding = new Thickness(10, 7, 10, 7);
        field.Background = Brush("#0A1B2D");
        field.Foreground = Brush("#EAF3FF");
        field.BorderBrush = Brush("#35516F");
        panel.Children.Add(field);
    }

    private void GenerateCredentials_Click(object sender, RoutedEventArgs e)
    {
        var number = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        _userName.Text = "sbx-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        var password = new char[22];
        for (var index = 0; index < password.Length; index++)
            password[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        _password.Password = new string(password);
        _confirmPassword.Password = new string(password);
        _copyCredentials.IsEnabled = true;
        _status.Text = "تم توليد بيانات فريدة لهذا التثبيت. انسخها واحفظها في مكان آمن قبل إغلاق النافذة.";
        _status.Foreground = Brush("#86EFAC");
    }

    private void CopyCredentials_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_userName.Text) || _password.Password.Length < SoftBoxAccountService.MinimumPasswordLength)
        {
            _status.Text = "أدخل اسم المستخدم وكلمة المرور أولًا.";
            _status.Foreground = Brush("#FCA5A5");
            return;
        }

        try
        {
            Clipboard.SetText("SoftBox username: " + _userName.Text.Trim() +
                Environment.NewLine + "SoftBox password: " + _password.Password);
            _status.Text = "تم نسخ بيانات الدخول. احفظها في مكان خاص ولا ترسلها في مجموعة عامة.";
            _status.Foreground = Brush("#86EFAC");
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر النسخ إلى الحافظة: " + ex.Message;
            _status.Foreground = Brush("#FCA5A5");
        }
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (_submit.IsEnabled == false)
            return;

        SetBusy(true);
        try
        {
            if (_initialSetup)
            {
                if (!string.Equals(_password.Password, _confirmPassword.Password, StringComparison.Ordinal))
                    throw new InvalidOperationException("كلمتا المرور غير متطابقتين.");

                AuthenticatedUser = await _service.CreateInitialAdministratorAsync(
                    _userName.Text, _displayName.Text, _password.Password);
            }
            else
            {
                AuthenticatedUser = await _service.AuthenticateAsync(_userName.Text, _password.Password);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _status.Foreground = Brush("#FCA5A5");
            _password.Clear();
            _confirmPassword.Clear();
            _password.Focus();
        }
        finally
        {
            if (IsVisible)
                SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _submit.IsEnabled = !busy;
        _userName.IsEnabled = !busy;
        _displayName.IsEnabled = !busy;
        _password.IsEnabled = !busy;
        _confirmPassword.IsEnabled = !busy;
        _status.Text = busy ? "جارٍ التحقق الآمن من بيانات الحساب..." : _status.Text;
    }

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
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
