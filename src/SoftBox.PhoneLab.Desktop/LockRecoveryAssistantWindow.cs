using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SoftBox.PhoneLab.Core.Security;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Owner-safe forgotten screen-lock guidance. It collects no PINs, passcodes or account secrets,
/// and performs no device command, reset, unlock, account removal or bypass.
/// </summary>
public sealed class LockRecoveryAssistantWindow : Window
{
    private readonly TextBox _manufacturer = new();
    private readonly TextBox _model = new();
    private readonly TextBox _osVersion = new();
    private readonly TextBox _oneUiVersion = new();
    private readonly ComboBox _platform = new();
    private readonly CheckBox _previousCredential = Check("المالك يعرف رمز القفل السابق");
    private readonly CheckBox _changedWithin72 = Check("تم تغيير رمز القفل خلال آخر 72 ساعة");
    private readonly CheckBox _accountAccess = Check("المالك يستطيع تسجيل الدخول إلى الحساب المرتبط بالجهاز");
    private readonly CheckBox _findMy = Check("خدمة Find My / Find Hub مفعّلة");
    private readonly CheckBox _backup = Check("توجد نسخة احتياطية يمكن استعادتها");
    private readonly CheckBox _ownership = Check("لدي إثبات ملكية أو تفويض صيانة موثق");
    private readonly TextBlock _status = TextBlockStyle("#9FC7E8", 12);
    private readonly TextBlock _result = TextBlockStyle("#DCEBFA", 12);
    private string? _officialUrl;

    public LockRecoveryAssistantWindow(string? manufacturer = null, string? model = null)
    {
        Title = "SoftBox | Owner Lock Recovery Assistant";
        Width = 900;
        Height = 820;
        MinWidth = 720;
        MinHeight = 620;
        FlowDirection = FlowDirection.RightToLeft;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _manufacturer.Text = manufacturer ?? string.Empty;
        _model.Text = model ?? string.Empty;
        _platform.Items.Add(Platform("Android", "android"));
        _platform.Items.Add(Platform("iOS", "ios"));
        _platform.Items.Add(Platform("iPadOS", "ipados"));
        _platform.Items.Add(Platform("HarmonyOS", "harmonyos"));
        _platform.Items.Add(Platform("نظام آخر", "other"));
        _platform.SelectedIndex = 0;

        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock
        {
            Text = "مساعد استرداد قفل الجهاز للمالك",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        root.Children.Add(Text(
            "هذا المحرك يختار مسار الاسترداد الرسمي فقط بناءً على النظام والشركة ووجود إثبات الملكية. لا يقرأ أو يخمّن رمز القفل، ولا يزيل FRP أو حساب Apple/Samsung/Mi أو إدارة المؤسسة، ولا ينفذ Factory Reset.",
            "#B7C9DE", 11));

        root.Children.Add(Warning(
            "لا تكتب PIN أو Pattern أو كلمة مرور أو رمز تحقق في SoftBox. بعض إجراءات الاسترداد الرسمية قد تمسح بيانات الهاتف، وقد تطلب الحساب السابق بعد إعادة الضبط. لا تبدأ مسحًا قبل التأكد من نسخة احتياطية وإمكانية الدخول للحساب."));

        AddField(root, "الشركة المصنّعة", _manufacturer, "Samsung / Huawei / OPPO / Xiaomi...");
        AddField(root, "الموديل", _model, "رقم الموديل إن كان معروفًا");
        root.Children.Add(Text("نظام التشغيل", "#7DD3FC", 10));
        StyleComboBox(_platform);
        root.Children.Add(_platform);
        AddField(root, "إصدار النظام", _osVersion, "مثال: 14 أو 17.4");
        AddField(root, "إصدار One UI (لأجهزة Samsung)", _oneUiVersion, "مثال: 6.1");

        root.Children.Add(Text("معلومات الاسترداد المتاحة للمالك", "#7DD3FC", 12));
        root.Children.Add(_previousCredential);
        root.Children.Add(_changedWithin72);
        root.Children.Add(_accountAccess);
        root.Children.Add(_findMy);
        root.Children.Add(_backup);
        root.Children.Add(_ownership);

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 10) };
        actions.Children.Add(Button("تحليل مسار الاسترداد الرسمي", "#14517A", (_, _) => Evaluate()));
        actions.Children.Add(Button("فتح دليل الشركة الرسمي", "#20364F", (_, _) => OpenOfficialGuide()));
        root.Children.Add(actions);

        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 7, 0, 7);
        root.Children.Add(_status);

        var resultCard = new Border
        {
            Background = Brush("#10243A"),
            BorderBrush = Brush("#2D4A68"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8, 0, 0),
            Child = _result
        };
        root.Children.Add(resultCard);
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void Evaluate()
    {
        var selectedPlatform = (_platform.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "android";
        var context = new LockRecoveryContext(
            selectedPlatform,
            _manufacturer.Text.Trim(),
            _osVersion.Text.Trim(),
            _oneUiVersion.Text.Trim(),
            _previousCredential.IsChecked == true,
            _changedWithin72.IsChecked == true,
            _accountAccess.IsChecked == true,
            _findMy.IsChecked == true,
            _backup.IsChecked == true,
            _ownership.IsChecked == true);

        var advice = LockRecoveryDecisionEngine.Evaluate(context);
        _officialUrl = advice.OfficialUrl;
        var risk = advice.DataLossPossible
            ? "⚠ احتمال فقدان البيانات: هذا المسار قد يمسح محتويات الجهاز."
            : "لم يقترح المحرك مسح الجهاز في هذه الخطوة.";
        var backup = _backup.IsChecked == true
            ? "المالك أفاد بوجود نسخة احتياطية؛ يجب التحقق من أنها حديثة وقابلة للاستعادة."
            : "لم يتم تأكيد وجود نسخة احتياطية.";

        _result.Text =
            advice.Title + Environment.NewLine + Environment.NewLine +
            advice.Explanation + Environment.NewLine + Environment.NewLine +
            "الخطوات: " + advice.Steps + Environment.NewLine + Environment.NewLine +
            risk + Environment.NewLine + backup + Environment.NewLine + Environment.NewLine +
            "المصدر الرسمي: " + advice.OfficialUrl + Environment.NewLine +
            "مسار الاسترداد: " + advice.Route;
        _status.Text = "اكتمل تحليل الإرشادات فقط. لم يرسل SoftBox أي أمر للهاتف ولم يغيّر حالة القفل أو الحساب.";
        _status.Foreground = Brush("#7DDBAA");
    }

    private void OpenOfficialGuide()
    {
        if (!Uri.TryCreate(_officialUrl, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "حلّل الحالة أولًا حتى يحدد المحرك رابط الاسترداد الرسمي المناسب.";
            _status.Foreground = Brush("#F5C27A");
            return;
        }

        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            _status.Text = "تعذر فتح المصدر الرسمي: " + ex.Message;
            _status.Foreground = Brush("#FFB4A6");
        }
    }

    private static ComboBoxItem Platform(string label, string value)
        => new() { Content = label, Tag = value, Padding = new Thickness(6) };

    private static CheckBox Check(string text)
        => new() { Content = text, IsChecked = false, Foreground = Brush("#DCEBFA"), Margin = new Thickness(2, 5, 2, 5) };

    private static void AddField(Panel parent, string label, TextBox field, string placeholder)
    {
        parent.Children.Add(Text(label, "#7DD3FC", 10));
        field.TextWrapping = TextWrapping.NoWrap;
        field.Padding = new Thickness(8);
        field.Margin = new Thickness(0, 3, 0, 7);
        field.Background = Brush("#0F1E32");
        field.Foreground = Brush("#EAF3FF");
        field.BorderBrush = Brush("#36516D");
        field.ToolTip = placeholder;
        parent.Children.Add(field);
    }

    private static void StyleComboBox(ComboBox combo)
    {
        combo.Background = Brush("#0F1E32");
        combo.Foreground = Brush("#EAF3FF");
        combo.BorderBrush = Brush("#36516D");
        combo.Padding = new Thickness(8);
        combo.Margin = new Thickness(0, 3, 0, 7);
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
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Background = Brush(color),
            Foreground = Brushes.White,
            BorderBrush = Brush("#3E7096")
        };
        button.Click += handler;
        return button;
    }

    private static TextBlock Text(string value, string color, double size)
        => new() { Text = value, Foreground = Brush(color), FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 4) };

    private static TextBlock TextBlockStyle(string color, double size)
        => new() { Foreground = Brush(color), FontSize = size, TextWrapping = TextWrapping.Wrap };

    private static SolidColorBrush Brush(string value)
        => new((Color)ColorConverter.ConvertFromString(value)!);
}
