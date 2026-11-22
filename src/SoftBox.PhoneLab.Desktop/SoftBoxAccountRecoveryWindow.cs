using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Security;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Owner-approved local account recovery and account-database reset.
/// Every operation requires a separate installation-bound, one-time signed approval.
/// </summary>
public sealed class SoftBoxAccountRecoveryWindow : Window
{
    private const string SupportWhatsAppUrl = "https://wa.me/201121241941?text=";
    private const int MaximumApprovalFileBytes = 65_536;

    private readonly SoftBoxAccountService _accounts;
    private readonly SoftBoxSupportAuthorizationService _authorization;
    private readonly TextBox _userName = new();
    private readonly TextBox _recoveryApproval = new();
    private readonly TextBox _resetApproval = new();
    private readonly PasswordBox _newPassword = new();
    private readonly PasswordBox _confirmPassword = new();
    private readonly TextBlock _status = new();
    private readonly Button _recoverPassword = new();
    private readonly Button _resetDatabase = new();
    private readonly TabControl _tabs = new();

    public SoftBoxAccountRecoveryWindow(SoftBoxAccountService service)
    {
        _accounts = service ?? throw new ArgumentNullException(nameof(service));
        _authorization = new SoftBoxSupportAuthorizationService();
        Title = "SoftBox | استرداد الحساب وتصفير الحسابات بموافقة الدعم";
        Width = 790;
        Height = 900;
        MinWidth = 620;
        MinHeight = 650;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(22), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "مركز استرداد حسابات SoftBox",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "اختر الإجراء المطلوب. لكل إجراء طلب وموافقة موقّعة مستقلان، ولا يُنفّذ أي تغيير قبل التحقق من توقيع المالك. لكل تثبيت طلب معلّق واحد؛ إنشاء طلب جديد يستبدل الطلب السابق، فأكمل الطلب الحالي أولًا.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 7, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        _status.Text = "ابدأ بإنشاء طلب خاص بهذا التثبيت، ثم أرسل JSON إلى الدعم. بعد استلام الموافقة الموقّعة استورد ملفها في التبويب المناسب.";
        _status.Foreground = Brush("#F0C9A5");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 12, 0, 0);
        _status.Padding = new Thickness(12, 10, 12, 10);
        _status.Background = Brush("#13253A");
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        _tabs.Background = Brush("#0D1C2E");
        _tabs.BorderBrush = Brush("#263D58");
        _tabs.Items.Add(BuildRecoveryTab());
        _tabs.Items.Add(BuildResetTab());
        root.Children.Add(_tabs);
        return root;
    }

    private TabItem BuildRecoveryTab()
    {
        var form = new StackPanel { Margin = new Thickness(14) };
        form.Children.Add(SectionTitle("01  |  استرداد كلمة مرور حساب واحد"));
        form.Children.Add(InfoText(
            "يُستخدم هذا الإجراء عندما تعرف اسم الحساب المحلي وتريد تعيين كلمة مرور جديدة له. لا يغيّر بقية الحسابات ولا يحذف قاعدة البيانات."));

        AddTextField(form, "اسم المستخدم المطلوب استرداده", _userName, "أدخل اسم المستخدم المحلي كما هو");
        var request = ButtonControl("إنشاء طلب استرداد وإرساله عبر WhatsApp", "#14517A", RequestPasswordRecovery_Click);
        request.Margin = new Thickness(0, 10, 0, 14);
        form.Children.Add(request);

        form.Children.Add(SectionTitle("استيراد موافقة المالك"));
        form.Children.Add(InfoText("اختر ملف JSON الذي أعاده مالك SoftBox بعد توقيع طلبك. يمكنك أيضًا لصق محتوى الموافقة يدويًا في الحقل."));
        form.Children.Add(BuildApprovalImportRow(_recoveryApproval, SoftBoxSupportAction.PasswordRecovery));
        ConfigureApprovalBox(_recoveryApproval);
        form.Children.Add(_recoveryApproval);

        AddPasswordField(form, "كلمة المرور الجديدة", _newPassword);
        AddPasswordField(form, "تأكيد كلمة المرور الجديدة", _confirmPassword);
        form.Children.Add(InfoText($"الحد الأدنى لكلمة المرور {SoftBoxAccountService.MinimumPasswordLength} حرفًا. لا تُرسل كلمة المرور الجديدة إلى الدعم أو عبر WhatsApp."));

        _recoverPassword.Content = "التحقق من الموافقة وتغيير كلمة المرور";
        StyleButton(_recoverPassword, "#176044");
        _recoverPassword.Height = 46;
        _recoverPassword.Margin = new Thickness(0, 14, 0, 0);
        _recoverPassword.Click += RecoverPassword_Click;
        form.Children.Add(_recoverPassword);

        return new TabItem
        {
            Header = "استرداد كلمة المرور",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = form
            }
        };
    }

    private TabItem BuildResetTab()
    {
        var form = new StackPanel { Margin = new Thickness(14) };
        form.Children.Add(SectionTitle("02  |  تصفير قاعدة حسابات SoftBox"));
        form.Children.Add(WarningText(
            "هذا الإجراء يحذف حسابات المستخدمين المحلية بالكامل، وبعده ستحتاج إلى إعادة فتح SoftBox وإنشاء حساب المدير الأول من جديد. لا يحذف ملفات البرنامج أو بيانات الأجهزة أو الإعدادات أو هوية التثبيت أو سجل منع إعادة استخدام الموافقات."));

        var request = ButtonControl("إنشاء طلب تصفير الحسابات وإرساله عبر WhatsApp", "#8A3D32", RequestDatabaseReset_Click);
        request.Margin = new Thickness(0, 12, 0, 14);
        form.Children.Add(request);

        form.Children.Add(SectionTitle("استيراد موافقة تصفير الحسابات"));
        form.Children.Add(InfoText("يجب أن تكون الموافقة صادرة خصيصًا لإجراء ResetLocalAccountDatabase وعلى هذا التثبيت والطلب الحالي."));
        form.Children.Add(BuildApprovalImportRow(_resetApproval, SoftBoxSupportAction.ResetLocalAccountDatabase));
        ConfigureApprovalBox(_resetApproval);
        form.Children.Add(_resetApproval);

        _resetDatabase.Content = "التحقق من الموافقة وتصفير الحسابات";
        StyleButton(_resetDatabase, "#8A3D32");
        _resetDatabase.Height = 46;
        _resetDatabase.Margin = new Thickness(0, 14, 0, 0);
        _resetDatabase.Click += ResetDatabase_Click;
        form.Children.Add(_resetDatabase);

        return new TabItem
        {
            Header = "تصفير / حذف الحسابات",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = form
            }
        };
    }

    private UIElement BuildApprovalImportRow(TextBox target, SoftBoxSupportAction expectedAction)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 7, 0, 8)
        };
        var import = ButtonControl("اختيار ملف موافقة JSON", "#14517A",
            (_, _) => ImportApprovalFile(target, expectedAction));
        import.Width = 220;
        import.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(import);

        var clear = ButtonControl("مسح الحقل", "#24364B", (_, _) => target.Clear());
        clear.Width = 110;
        row.Children.Add(clear);
        return row;
    }

    private void ImportApprovalFile(TextBox target, SoftBoxSupportAction expectedAction)
    {
        var dialog = new OpenFileDialog
        {
            Title = "استيراد موافقة دعم SoftBox الموقّعة",
            Filter = "SoftBox approval JSON (*.json)|*.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length is <= 0 or > MaximumApprovalFileBytes)
                throw new InvalidDataException("حجم ملف الموافقة غير صالح أو تجاوز 64 كيلوبايت.");

            var json = File.ReadAllText(dialog.FileName);
            ValidateApprovalEnvelope(json, expectedAction, _userName.Text);
            target.Text = json;
            _status.Text = "تم استيراد ملف الموافقة وفحص بنيته ونوع الإجراء. سيجري التحقق من التوقيع والتثبيت والصلاحية عند التنفيذ.";
            _status.Foreground = Brush("#86EFAC");
        }
        catch (Exception ex)
        {
            SetError("تعذر استيراد ملف الموافقة: " + ex.Message);
        }
    }

    private static void ValidateApprovalEnvelope(
        string json, SoftBoxSupportAction expectedAction, string? expectedUserName)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 12
        });

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("requestId", out _) &&
            root.TryGetProperty("installationId", out _) &&
            root.TryGetProperty("nonce", out _) &&
            !root.TryGetProperty("payload", out _))
        {
            throw new InvalidDataException(
                "هذا هو طلب الدعم الأصلي الذي أنشأه SoftBox، وليس الموافقة الموقّعة. أرسل هذا الطلب إلى تطبيق SoftBox Support Key Manager عند المالك، ثم استورد ملف الموافقة الذي يعيده لك ويحتوي على payload و signature.");
        }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("signature", out var signature) ||
            signature.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(signature.GetString()))
            throw new InvalidDataException(
                "ملف الموافقة غير مكتمل. الموافقة الصحيحة يجب أن تحتوي على payload و signature صادرين من تطبيق المالك.");

        if (!payload.TryGetProperty("action", out var action) ||
            !string.Equals(action.GetString(), expectedAction.ToString(), StringComparison.Ordinal))
            throw new InvalidDataException("نوع الإجراء في الموافقة لا يطابق التبويب المحدد.");

        if (expectedAction == SoftBoxSupportAction.PasswordRecovery)
        {
            if (!payload.TryGetProperty("subjectUserName", out var subject) ||
                !string.Equals(subject.GetString(), (expectedUserName ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("الموافقة ليست صادرة لاسم المستخدم المكتوب في حقل الاسترداد.");
        }
        else if (payload.TryGetProperty("subjectUserName", out var subject) &&
                 !string.IsNullOrEmpty(subject.GetString()))
        {
            throw new InvalidDataException("موافقة تصفير الحسابات يجب ألا ترتبط باسم مستخدم.");
        }
    }

    private void RequestPasswordRecovery_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var request = _authorization.CreatePasswordRecoveryRequest(_userName.Text);
            _recoveryApproval.Clear();
            OpenWhatsAppRequest(request, "طلب استرداد كلمة مرور حساب SoftBox");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void RequestDatabaseReset_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this,
            "سيُنشأ طلب منفصل لتصفير حسابات SoftBox المحلية. إنشاء الطلب لا يحذف أي حساب الآن؛ الحذف لا يحدث إلا بعد موافقة المالك الموقّعة وتأكيد نهائي منك. هل تريد المتابعة؟",
            "تأكيد إنشاء طلب تصفير الحسابات", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var request = _authorization.CreateDatabaseResetRequest();
            _resetApproval.Clear();
            OpenWhatsAppRequest(request, "طلب تصفير قاعدة حسابات SoftBox المحلية");
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private void OpenWhatsAppRequest(string requestJson, string title)
    {
        var message = title + Environment.NewLine +
            "يرجى مراجعة طلب JSON وإرساله كما هو. لا يحتوي الطلب على كلمة المرور:" +
            Environment.NewLine + requestJson;
        var copied = false;
        try
        {
            Clipboard.SetText(requestJson);
            copied = true;
        }
        catch { /* The WhatsApp prefilled message still includes the complete request JSON. */ }

        try
        {
            Process.Start(new ProcessStartInfo(SupportWhatsAppUrl + Uri.EscapeDataString(message))
            {
                UseShellExecute = true
            });
            _status.Text = copied
                ? "تم إنشاء الطلب ونسخه إلى الحافظة. راجع المحادثة والمستلم ثم اضغط إرسال بنفسك."
                : "تم إنشاء الطلب وفتح WhatsApp. راجع المحادثة والمستلم ثم اضغط إرسال بنفسك.";
            _status.Foreground = Brush("#86EFAC");
        }
        catch (Exception ex)
        {
            _status.Text = (copied ? "تم إنشاء الطلب ونسخه إلى الحافظة، " : "تم إنشاء الطلب، ") +
                "لكن تعذر فتح WhatsApp تلقائيًا: " + ex.Message;
            _status.Foreground = Brush("#F0C9A5");
        }
    }

    private async void RecoverPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!_recoverPassword.IsEnabled) return;

        if (!string.Equals(_newPassword.Password, _confirmPassword.Password, StringComparison.Ordinal))
        {
            SetError("كلمتا المرور الجديدتان غير متطابقتين.");
            return;
        }

        try { SoftBoxAccountService.ValidatePassword(_newPassword.Password); }
        catch (Exception ex) { SetError(ex.Message); return; }

        var confirm = MessageBox.Show(this,
            $"سيتم تغيير كلمة مرور الحساب «{_userName.Text.Trim()}» فقط بعد التحقق من موافقة المالك الموقّعة، وأنها مرتبطة بهذا التثبيت والطلب واسم المستخدم. هل توافق على تنفيذ الاسترداد؟",
            "تأكيد استرداد كلمة المرور", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
        {
            var user = await _authorization.RecoverPasswordAsync(
                _accounts, _recoveryApproval.Text, _userName.Text, _newPassword.Password);
            if (Owner is SoftBoxAuthenticationWindow loginWindow)
                loginWindow.PrepareForRecoveredPasswordLogin(user.UserName);

            MessageBox.Show(this,
                $"تم تغيير كلمة مرور الحساب {user.UserName} بنجاح. ارجع إلى شاشة الدخول واكتب كلمة المرور الجديدة؛ لا تستخدم كلمة المرور القديمة.",
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
            "تأكيد نهائي: سيُحذف ملف حسابات المستخدمين المحلي بالكامل. ستبقى ملفات البرنامج وبيانات الأجهزة والإعدادات وهوية التثبيت وسجل منع إعادة استخدام الموافقات. بعد ذلك ستحتاج إلى إنشاء حساب مدير جديد. لا يمكن التراجع عن حذف الحسابات. هل توافق؟",
            "تأكيد نهائي لتصفير قاعدة الحسابات", MessageBoxButton.YesNo, MessageBoxImage.Stop);
        if (confirm != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
        {
            await _authorization.ResetLocalAccountDatabaseAsync(_accounts, _resetApproval.Text);
            MessageBox.Show(this,
                "تم تصفير قاعدة الحسابات المحلية بنجاح. سيُعاد تشغيل SoftBox الآن لفتح شاشة إنشاء حساب المدير الجديد. يمكنك توليد اسم مستخدم وكلمة مرور عشوائيين ونسخهما قبل إكمال التسجيل. الموافقة المستهلكة لا يمكن استخدامها مرة أخرى.",
                "SoftBox | اكتمل التصفير", MessageBoxButton.OK, MessageBoxImage.Information);

            var executable = Path.Combine(AppContext.BaseDirectory, "SoftBox-PhoneLab.exe");
            if (!File.Exists(executable))
                executable = Environment.ProcessPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
            {
                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show(this,
                    "تم التصفير. أغلق SoftBox ثم شغّله يدويًا لفتح شاشة إنشاء حساب المدير الجديد.",
                    "SoftBox | إعادة التشغيل", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Application.Current.Shutdown(0);
        }
        catch (Exception ex) { SetError(ex.Message); }
        finally { if (IsVisible) SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _recoverPassword.IsEnabled = !busy;
        _resetDatabase.IsEnabled = !busy;
        _tabs.IsEnabled = !busy;
    }

    private static void ConfigureApprovalBox(TextBox field)
    {
        field.Height = 150;
        field.AcceptsReturn = true;
        field.TextWrapping = TextWrapping.Wrap;
        field.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        field.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        field.FontFamily = new FontFamily("Consolas");
        field.FontSize = 11;
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

    private static TextBlock SectionTitle(string text)
        => new()
        {
            Text = text,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC"),
            Margin = new Thickness(0, 0, 0, 8)
        };

    private static TextBlock InfoText(string text)
        => new()
        {
            Text = text,
            FontSize = 11,
            Foreground = Brush("#B7C9DE"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

    private static TextBlock WarningText(string text)
        => new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#FFD4BD"),
            Background = Brush("#3A211F"),
            Padding = new Thickness(12),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 10)
        };

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 9, 12, 9),
            Background = Brush(color),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold,
            MinHeight = 42
        };
        button.Click += handler;
        return button;
    }

    private static void StyleButton(Button button, string color)
    {
        button.Padding = new Thickness(12, 9, 12, 9);
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
