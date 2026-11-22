using Microsoft.Win32;
using SoftBox.PhoneLab.Core.Security;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Administrator-only, one-time export of installation-bound support recovery codes.
/// Plaintext is shown only in this window and is never persisted by the application.
/// </summary>
public sealed class SupportRecoveryCodesWindow : Window
{
    private readonly SoftBoxAccountService _service;
    private readonly AuthenticatedSoftBoxUser _actor;
    private readonly TextBox _codes = new();
    private readonly TextBlock _status = new();
    private readonly Button _generate = new();
    private readonly Button _save = new();
    private string[] _issuedCodes = [];

    public SupportRecoveryCodesWindow(SoftBoxAccountService service, AuthenticatedSoftBoxUser actor)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        if (actor.Role != SoftBoxUserRole.Administrator)
            throw new UnauthorizedAccessException("إصدار رموز الدعم متاح للمدير فقط.");

        Title = "SoftBox | رموز استرداد الدعم";
        Width = 760;
        Height = 760;
        MinWidth = 620;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(20), LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "إصدار رموز استرداد الحساب",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "ADMIN ONLY • THIS INSTALLATION • SINGLE USE",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "سيتم توليد 1000 رمز عشوائي خاص بقاعدة حسابات هذا التثبيت. قاعدة البيانات تحفظ بصمات HMAC محمية فقط، ولا تستطيع عرض الرموز مرة أخرى بعد إغلاق هذه النافذة. احتفظ بملف التصدير في مكان آمن ولا تشاركه علنًا.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#F0C9A5"),
            Margin = new Thickness(0, 10, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _generate.Content = "توليد 1000 رمز جديد";
        StyleButton(_generate, "#176044");
        _generate.Click += Generate_Click;
        _save.Content = "حفظ الرموز في ملف TXT";
        StyleButton(_save, "#14517A");
        _save.Margin = new Thickness(8, 0, 0, 0);
        _save.IsEnabled = false;
        _save.Click += Save_Click;
        actions.Children.Add(_generate);
        actions.Children.Add(_save);
        footer.Children.Add(actions);
        _status.Text = "لم يتم إصدار رموز في هذه الجلسة.";
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 10, 0, 0);
        footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        _codes.IsReadOnly = true;
        _codes.AcceptsReturn = true;
        _codes.TextWrapping = TextWrapping.NoWrap;
        _codes.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _codes.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _codes.FontFamily = new FontFamily("Consolas");
        _codes.FontSize = 11;
        _codes.Background = Brush("#071625");
        _codes.Foreground = Brush("#DCEBFA");
        _codes.BorderBrush = Brush("#263D58");
        _codes.Padding = new Thickness(12);
        _codes.Text = "اضغط «توليد 1000 رمز جديد» لإصدار دفعة لهذه القاعدة المحلية.";
        root.Children.Add(_codes);
        return root;
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_issuedCodes.Length > 0 &&
            MessageBox.Show(this,
                "توجد رموز تم إصدارها بالفعل في هذه الجلسة. إصدار دفعة أخرى سيضيف رموزًا جديدة ولن يلغي الرموز السابقة. هل تريد المتابعة؟",
                "إصدار رموز إضافية", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _generate.IsEnabled = false;
        _save.IsEnabled = false;
        try
        {
            var codes = await _service.GenerateSupportRecoveryCodesAsync(_actor.Id, 1_000);
            _issuedCodes = codes.ToArray();
            _codes.Text = string.Join(Environment.NewLine, _issuedCodes);
            _save.IsEnabled = true;
            _status.Text = "تم إصدار 1000 رمز. احفظ الملف الآن؛ لن يتمكن SoftBox من عرض النص الأصلي مرة أخرى.";
            _status.Foreground = Brush("#86EFAC");
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر إصدار الرموز: " + ex.Message;
            _status.Foreground = Brush("#FCA5A5");
            MessageBox.Show(this, ex.Message, "SoftBox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _generate.IsEnabled = true; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_issuedCodes.Length == 0)
            return;
        var dialog = new SaveFileDialog
        {
            Title = "حفظ رموز استرداد SoftBox",
            Filter = "Text file (*.txt)|*.txt",
            FileName = $"SoftBox-Recovery-Codes-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            AddExtension = true,
            DefaultExt = ".txt"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var text = "SOFTBOX INSTALLATION RECOVERY CODES" + Environment.NewLine +
                       "Keep this file private. Every code can be used once for this installation only." +
                       Environment.NewLine + Environment.NewLine +
                       string.Join(Environment.NewLine, _issuedCodes);
            await File.WriteAllTextAsync(dialog.FileName, text);
            _status.Text = "تم حفظ الرموز. أرسل الرمز المطلوب فقط للمستخدم بعد التحقق من ملكية الحساب، ولا ترسل الدفعة كاملة.";
            _status.Foreground = Brush("#86EFAC");
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر حفظ الملف: " + ex.Message;
            _status.Foreground = Brush("#FCA5A5");
        }
    }

    private static void StyleButton(Button button, string color)
    {
        button.Padding = new Thickness(12, 8, 12, 8);
        button.Background = Brush(color);
        button.Foreground = Brushes.White;
        button.BorderBrush = Brush("#3E7096");
        button.FontWeight = FontWeights.SemiBold;
    }

    private static SolidColorBrush Brush(string color)
        => new((Color)ColorConverter.ConvertFromString(color));
}
