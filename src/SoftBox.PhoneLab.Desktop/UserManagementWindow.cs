using SoftBox.PhoneLab.Core.Security;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Security.Cryptography;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Administrator-only local user, role, password and audit management.
/// Accounts are disabled rather than deleted so their audit history remains attributable.
/// </summary>
public sealed class UserManagementWindow : Window
{
    private readonly SoftBoxAccountService _service;
    private readonly AuthenticatedSoftBoxUser _actor;
    private readonly ObservableCollection<SoftBoxUserSummary> _users = [];
    private readonly ObservableCollection<SoftBoxUserAuditEntry> _audit = [];
    private readonly DataGrid _usersGrid = new();
    private readonly DataGrid _auditGrid = new();
    private readonly TextBox _newUserName = new();
    private readonly TextBox _newDisplayName = new();
    private readonly PasswordBox _newPassword = new();
    private readonly Button _copyNewCredentials = new();
    private readonly ComboBox _newRole = new();
    private readonly ComboBox _selectedRole = new();
    private readonly CheckBox _selectedEnabled = new();
    private readonly PasswordBox _resetPassword = new();
    private readonly PasswordBox _currentPassword = new();
    private readonly PasswordBox _changedPassword = new();
    private readonly PasswordBox _confirmChangedPassword = new();
    private readonly TextBlock _status = new();
    private readonly Button _refreshButton;

    public UserManagementWindow(SoftBoxAccountService service, AuthenticatedSoftBoxUser actor)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        if (actor.Role != SoftBoxUserRole.Administrator)
            throw new UnauthorizedAccessException("إدارة المستخدمين متاحة للمدير فقط.");

        Title = "SoftBox | المستخدمون والفنيون والصلاحيات";
        Width = 1120;
        Height = 820;
        MinWidth = 900;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _refreshButton = ButtonControl("تحديث القائمة", "#14517A", Refresh_Click);
        Content = BuildContent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "إدارة المستخدمين والفنيين",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = $"ADMINISTRATOR • {_actor.DisplayName} • LOCAL ACCOUNTS • ROLE-BASED ACCESS",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "إدارة الحسابات والصلاحيات محلية على هذا التثبيت. تعطيل الحساب يحتفظ بسجله، ولا يمكن تعطيل أو تخفيض صلاحية آخر مدير نشط.",
            FontSize = 11,
            Foreground = Brush("#B7C9DE"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Text = "يتم تحميل الحسابات وسجل التدقيق المحلي...";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(_status);
        var recoveryCodes = ButtonControl("إصدار 1000 رمز استرداد للدعم لهذا التثبيت", "#14517A",
            (_, _) => new SupportRecoveryCodesWindow(_service, _actor) { Owner = this }.Show());
        recoveryCodes.HorizontalAlignment = HorizontalAlignment.Left;
        recoveryCodes.Margin = new Thickness(0, 8, 0, 0);
        footer.Children.Add(recoveryCodes);
        _refreshButton.HorizontalAlignment = HorizontalAlignment.Left;
        _refreshButton.Margin = new Thickness(0, 8, 0, 0);
        footer.Children.Add(_refreshButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var tabs = new TabControl
        {
            Background = Brush("#0D1C2E"),
            BorderBrush = Brush("#263D58")
        };
        tabs.Items.Add(BuildAccountsTab());
        tabs.Items.Add(BuildAuditTab());
        tabs.Items.Add(BuildPasswordTab());
        root.Children.Add(tabs);
        return root;
    }

    private TabItem BuildAccountsTab()
    {
        _usersGrid.AutoGenerateColumns = false;
        _usersGrid.IsReadOnly = true;
        _usersGrid.SelectionMode = DataGridSelectionMode.Single;
        _usersGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _usersGrid.ItemsSource = _users;
        _usersGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _usersGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _usersGrid.Background = Brush("#0A1828");
        _usersGrid.Foreground = Brush("#DCEBFA");
        _usersGrid.RowBackground = Brush("#102238");
        _usersGrid.AlternatingRowBackground = Brush("#0C1C30");
        _usersGrid.BorderBrush = Brush("#263D58");
        _usersGrid.Columns.Add(TextColumn("اسم المستخدم", nameof(SoftBoxUserSummary.UserName), 120));
        _usersGrid.Columns.Add(TextColumn("الاسم الظاهر", nameof(SoftBoxUserSummary.DisplayName), 150));
        _usersGrid.Columns.Add(TextColumn("الدور", nameof(SoftBoxUserSummary.Role), 105));
        _usersGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "نشط",
            Binding = new System.Windows.Data.Binding(nameof(SoftBoxUserSummary.IsEnabled)),
            Width = 65
        });
        _usersGrid.Columns.Add(TextColumn("آخر دخول UTC", nameof(SoftBoxUserSummary.LastLoginAtUtc), 175));
        _usersGrid.Columns.Add(TextColumn("محاولات فاشلة", nameof(SoftBoxUserSummary.FailedLoginAttempts), 100));

        _usersGrid.SelectionChanged += (_, _) => LoadSelectedUser();
        var layout = new Grid { Margin = new Thickness(8) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(_usersGrid);

        var actions = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(new TextBlock
        {
            Text = "إنشاء حساب جديد",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var createGrid = new Grid();
        createGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        createGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        createGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        createGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        AddLabeledControl(createGrid, 0, "اسم المستخدم", _newUserName);
        AddLabeledControl(createGrid, 1, "الاسم الظاهر", _newDisplayName);
        AddLabeledControl(createGrid, 2, $"كلمة المرور ({SoftBoxAccountService.MinimumPasswordLength}+ حرفًا)", _newPassword);
        _newRole.ItemsSource = Enum.GetValues<SoftBoxUserRole>();
        _newRole.SelectedItem = SoftBoxUserRole.Technician;
        AddLabeledControl(createGrid, 3, "الدور", _newRole);
        actions.Children.Add(createGrid);

        var generatedActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        generatedActions.Children.Add(ButtonControl("توليد اسم مستخدم وكلمة مرور", "#14517A", GenerateNewUserCredentials_Click));
        _copyNewCredentials.Content = "نسخ بيانات الحساب";
        _copyNewCredentials.Background = Brush("#24364B");
        _copyNewCredentials.Foreground = Brushes.White;
        _copyNewCredentials.BorderBrush = Brush("#3E7096");
        _copyNewCredentials.Padding = new Thickness(10, 7, 10, 7);
        _copyNewCredentials.Margin = new Thickness(8, 0, 0, 0);
        _copyNewCredentials.IsEnabled = false;
        _copyNewCredentials.Click += CopyNewUserCredentials_Click;
        generatedActions.Children.Add(_copyNewCredentials);
        actions.Children.Add(generatedActions);

        var createButton = ButtonControl("إنشاء الحساب", "#176044", CreateUser_Click);
        createButton.HorizontalAlignment = HorizontalAlignment.Left;
        createButton.Margin = new Thickness(0, 10, 0, 12);
        actions.Children.Add(createButton);

        actions.Children.Add(new DividerControl());
        actions.Children.Add(new TextBlock
        {
            Text = "تعديل الحساب المحدد",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 8, 0, 6)
        });

        var selectedRow = new StackPanel { Orientation = Orientation.Horizontal };
        _selectedRole.ItemsSource = Enum.GetValues<SoftBoxUserRole>();
        _selectedRole.SelectedItem = SoftBoxUserRole.Technician;
        _selectedRole.Width = 170;
        _selectedRole.Margin = new Thickness(0, 0, 10, 0);
        StyleCombo(_selectedRole);
        selectedRow.Children.Add(_selectedRole);
        _selectedEnabled.Content = "الحساب نشط";
        _selectedEnabled.IsChecked = true;
        _selectedEnabled.Foreground = Brush("#DCEBFA");
        _selectedEnabled.VerticalAlignment = VerticalAlignment.Center;
        _selectedEnabled.Margin = new Thickness(8, 0, 14, 0);
        selectedRow.Children.Add(_selectedEnabled);
        selectedRow.Children.Add(ButtonControl("تطبيق الدور والحالة", "#14517A", UpdateUser_Click));
        actions.Children.Add(selectedRow);

        actions.Children.Add(new TextBlock
        {
            Text = "إعادة تعيين كلمة مرور حساب آخر",
            FontSize = 11,
            Foreground = Brush("#B7C9DE"),
            Margin = new Thickness(0, 10, 0, 5)
        });
        var resetRow = new StackPanel { Orientation = Orientation.Horizontal };
        _resetPassword.Width = 270;
        _resetPassword.Margin = new Thickness(0, 0, 10, 0);
        StylePassword(_resetPassword);
        resetRow.Children.Add(_resetPassword);
        resetRow.Children.Add(ButtonControl("إعادة تعيين كلمة المرور", "#6B3D22", ResetPassword_Click));
        actions.Children.Add(resetRow);

        Grid.SetRow(actions, 1);
        layout.Children.Add(actions);
        return new TabItem { Header = "الحسابات والصلاحيات", Content = layout };
    }

    private TabItem BuildAuditTab()
    {
        _auditGrid.AutoGenerateColumns = false;
        _auditGrid.IsReadOnly = true;
        _auditGrid.ItemsSource = _audit;
        _auditGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _auditGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _auditGrid.Background = Brush("#0A1828");
        _auditGrid.Foreground = Brush("#DCEBFA");
        _auditGrid.RowBackground = Brush("#102238");
        _auditGrid.AlternatingRowBackground = Brush("#0C1C30");
        _auditGrid.BorderBrush = Brush("#263D58");
        _auditGrid.Columns.Add(TextColumn("التاريخ UTC", nameof(SoftBoxUserAuditEntry.AtUtc), 175));
        _auditGrid.Columns.Add(TextColumn("نوع الحدث", nameof(SoftBoxUserAuditEntry.EventType), 175));
        _auditGrid.Columns.Add(TextColumn("المرجع", nameof(SoftBoxUserAuditEntry.Reference), 140));
        _auditGrid.Columns.Add(TextColumn("التفاصيل", nameof(SoftBoxUserAuditEntry.Description), 430));
        return new TabItem { Header = "سجل التدقيق", Content = _auditGrid };
    }

    private TabItem BuildPasswordTab()
    {
        var panel = new StackPanel { Margin = new Thickness(20), MaxWidth = 540, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock
        {
            Text = "تغيير كلمة مرور حساب المدير الحالي",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC"),
            Margin = new Thickness(0, 0, 0, 12)
        });
        AddLabeledPanelControl(panel, "كلمة المرور الحالية", _currentPassword);
        AddLabeledPanelControl(panel, "كلمة المرور الجديدة", _changedPassword);
        AddLabeledPanelControl(panel, "تأكيد كلمة المرور الجديدة", _confirmChangedPassword);
        panel.Children.Add(ButtonControl("تغيير كلمة المرور", "#176044", ChangeOwnPassword_Click));
        panel.Children.Add(new TextBlock
        {
            Text = $"تُخزن كلمة المرور كقيمة تحقق PBKDF2-HMAC-SHA256 مع Salt عشوائي؛ لا يتم حفظ النص الأصلي. الحد الأدنى {SoftBoxAccountService.MinimumPasswordLength} حرفًا.",
            FontSize = 11,
            Foreground = Brush("#9CB3CC"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0)
        });
        return new TabItem { Header = "كلمة المرور", Content = panel };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            var users = await _service.GetUsersAsync(_actor.Id);
            var audit = await _service.GetAuditAsync(_actor.Id, 500);
            _users.Clear();
            foreach (var user in users)
                _users.Add(user);
            _audit.Clear();
            foreach (var entry in audit)
                _audit.Add(entry);
            _status.Text = $"الحسابات: {_users.Count} • سجلات التدقيق المعروضة: {_audit.Count} • لا يتم حذف حسابات المستخدمين من هذه الواجهة.";
            _status.Foreground = Brush("#86EFAC");
        }
        catch (Exception ex)
        {
            SetError("تعذر تحميل الحسابات وسجل التدقيق: " + ex.Message);
        }
    }

    private void LoadSelectedUser()
    {
        if (_usersGrid.SelectedItem is not SoftBoxUserSummary selected)
            return;
        _selectedRole.SelectedItem = selected.Role;
        _selectedEnabled.IsChecked = selected.IsEnabled;
        _status.Text = $"الحساب المحدد: {selected.UserName} • {SoftBoxAccountService.RoleLabel(selected.Role)}";
    }

    private void GenerateNewUserCredentials_Click(object sender, RoutedEventArgs e)
    {
        var number = RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        _newUserName.Text = "sbx-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
        var password = new char[22];
        for (var index = 0; index < password.Length; index++)
            password[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        _newPassword.Password = new string(password);
        _copyNewCredentials.IsEnabled = true;
        _status.Text = "تم توليد بيانات دخول عشوائية للحساب الجديد. انسخها وسلّمها لصاحب الحساب عبر قناة آمنة.";
        _status.Foreground = Brush("#86EFAC");
    }

    private void CopyNewUserCredentials_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_newUserName.Text) ||
            _newPassword.Password.Length < SoftBoxAccountService.MinimumPasswordLength)
        {
            SetError("أدخل اسم المستخدم وكلمة المرور أولًا.");
            return;
        }

        try
        {
            Clipboard.SetText("SoftBox username: " + _newUserName.Text.Trim() +
                Environment.NewLine + "SoftBox password: " + _newPassword.Password);
            _status.Text = "تم نسخ بيانات المستخدم الجديد.";
            _status.Foreground = Brush("#86EFAC");
        }
        catch (Exception ex) { SetError("تعذر نسخ بيانات الحساب: " + ex.Message); }
    }

    private async void CreateUser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var role = _newRole.SelectedItem is SoftBoxUserRole value ? value : SoftBoxUserRole.Technician;
            await _service.CreateUserAsync(_actor.Id, _newUserName.Text, _newDisplayName.Text, role, _newPassword.Password);
            _newPassword.Clear();
            _newUserName.Clear();
            _newDisplayName.Clear();
            _copyNewCredentials.IsEnabled = false;
            await RefreshAsync();
            _status.Text = "تم إنشاء الحساب وحفظ سجل التدقيق.";
        }
        catch (Exception ex) { SetError("تعذر إنشاء الحساب: " + ex.Message); }
    }

    private async void UpdateUser_Click(object sender, RoutedEventArgs e)
    {
        if (_usersGrid.SelectedItem is not SoftBoxUserSummary selected)
        {
            SetError("اختر حسابًا من القائمة أولًا.");
            return;
        }

        try
        {
            var role = _selectedRole.SelectedItem is SoftBoxUserRole value ? value : selected.Role;
            await _service.UpdateUserAsync(_actor.Id, selected.Id, selected.DisplayName, role, _selectedEnabled.IsChecked == true);
            await RefreshAsync();
            _status.Text = "تم تحديث الدور والحالة مع الاحتفاظ بسجل الحساب.";
        }
        catch (Exception ex) { SetError("تعذر تحديث الحساب: " + ex.Message); }
    }

    private async void ResetPassword_Click(object sender, RoutedEventArgs e)
    {
        if (_usersGrid.SelectedItem is not SoftBoxUserSummary selected)
        {
            SetError("اختر الحساب المطلوب أولًا.");
            return;
        }

        try
        {
            await _service.ResetUserPasswordAsync(_actor.Id, selected.Id, _resetPassword.Password);
            _resetPassword.Clear();
            await RefreshAsync();
            _status.Text = "تم تعيين كلمة مرور جديدة للحساب المحدد. سلّمها للمستخدم عبر قناة آمنة.";
        }
        catch (Exception ex) { SetError("تعذر إعادة تعيين كلمة المرور: " + ex.Message); }
    }

    private async void ChangeOwnPassword_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.Equals(_changedPassword.Password, _confirmChangedPassword.Password, StringComparison.Ordinal))
                throw new InvalidOperationException("كلمتا المرور الجديدتان غير متطابقتين.");

            await _service.ChangeOwnPasswordAsync(_actor.Id, _currentPassword.Password, _changedPassword.Password);
            _currentPassword.Clear();
            _changedPassword.Clear();
            _confirmChangedPassword.Clear();
            await RefreshAsync();
            _status.Text = "تم تغيير كلمة مرور حسابك.";
        }
        catch (Exception ex) { SetError("تعذر تغيير كلمة المرور: " + ex.Message); }
    }

    private void SetError(string message)
    {
        _status.Text = message;
        _status.Foreground = Brush("#FCA5A5");
    }

    private static DataGridTextColumn TextColumn(string header, string path, double width)
        => new()
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(path) { StringFormat = path.EndsWith("AtUtc", StringComparison.Ordinal) ? "yyyy-MM-dd HH:mm:ss" : null },
            Width = width
        };

    private static void AddLabeledControl(Grid grid, int column, string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10,
            Foreground = Brush("#9FC7E8"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        if (control is TextBox textBox)
        {
            textBox.Height = 34;
            textBox.Padding = new Thickness(8, 6, 8, 6);
            textBox.Background = Brush("#0A1B2D");
            textBox.Foreground = Brush("#EAF3FF");
            textBox.BorderBrush = Brush("#35516F");
        }
        else if (control is PasswordBox passwordBox)
        {
            StylePassword(passwordBox);
        }
        else if (control is ComboBox combo)
        {
            StyleCombo(combo);
        }
        panel.Children.Add(control);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }

    private static void AddLabeledPanelControl(Panel panel, string label, PasswordBox control)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = Brush("#9FC7E8"),
            Margin = new Thickness(0, 8, 0, 5)
        });
        StylePassword(control);
        control.Height = 38;
        panel.Children.Add(control);
    }

    private static void StylePassword(PasswordBox password)
    {
        password.Height = 34;
        password.Padding = new Thickness(8, 6, 8, 6);
        password.Background = Brush("#0A1B2D");
        password.Foreground = Brush("#EAF3FF");
        password.BorderBrush = Brush("#35516F");
    }

    private static void StyleCombo(ComboBox combo)
    {
        combo.Height = 34;
        combo.Background = Brush("#0A1B2D");
        combo.Foreground = Brush("#EAF3FF");
        combo.BorderBrush = Brush("#35516F");
    }

    private static Button ButtonControl(string label, string color, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            MinWidth = 125,
            Background = Brush(color),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096"),
            FontWeight = FontWeights.SemiBold
        };
        button.Click += handler;
        return button;
    }

    private sealed class DividerControl : Border
    {
        public DividerControl()
        {
            Height = 1;
            Background = Brush("#263D58");
            Margin = new Thickness(0, 6, 0, 4);
        }
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
