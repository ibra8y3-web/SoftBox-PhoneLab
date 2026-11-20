using SoftBox.PhoneLab.Core.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Application preferences for discovery, device automation and safe batch concurrency.
/// </summary>
public sealed class ApplicationSettingsWindow : Window
{
    private readonly PhoneLabSettingsService _service;
    private readonly Action<PhoneLabSettings> _apply;
    private readonly CheckBox _autoDiscover = new();
    private readonly ComboBox _interval = new();
    private readonly CheckBox _autoMirror = new();
    private readonly CheckBox _autoAgent = new();
    private readonly CheckBox _autoInspect = new();
    private readonly ComboBox _concurrency = new();
    private readonly TextBlock _status = new();

    public ApplicationSettingsWindow(
        PhoneLabSettingsService service,
        PhoneLabSettings current,
        Action<PhoneLabSettings> apply)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));

        Title = "SoftBox | إعدادات التطبيق";
        Width = 760;
        Height = 650;
        MinWidth = 620;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        ConfigureControls(current);
        Content = BuildContent();
    }

    private void ConfigureControls(PhoneLabSettings settings)
    {
        _autoDiscover.Content = "اكتشاف الأجهزة تلقائيًا أثناء تشغيل SoftBox";
        _autoDiscover.IsChecked = settings.AutoDiscoverDevices;

        _interval.ItemsSource = new[] { 3, 5, 10, 15, 30, 60, 90, 120 };
        _interval.SelectedItem = settings.DiscoveryIntervalSeconds;
        if (_interval.SelectedItem is null)
            _interval.SelectedItem = 3;

        _autoMirror.Content = "فتح عرض شاشة الهاتف تلقائيًا عند الاتصال";
        _autoMirror.IsChecked = settings.AutoOpenScreenMirror;

        _autoAgent.Content = "تثبيت / تجهيز PhoneLab Agent تلقائيًا بعد تصريح ADB";
        _autoAgent.IsChecked = settings.AutoInstallAgent;

        _autoInspect.Content = "قراءة معلومات الجهاز تلقائيًا بعد الاتصال";
        _autoInspect.IsChecked = settings.AutoInspectOnConnect;

        _concurrency.ItemsSource = Enumerable.Range(1, 8).ToArray();
        _concurrency.SelectedItem = settings.MaximumBatchConcurrency;
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(new TextBlock
        {
            Text = "إعدادات SoftBox",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "LOCAL USER PREFERENCES • DEVICE AUTOMATION • SAFE CONCURRENCY",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var content = new StackPanel();
        AddCheck(content, _autoDiscover);
        AddCombo(content, "فترة اكتشاف الأجهزة (ثانية)", _interval);
        AddCheck(content, _autoInspect);
        AddCheck(content, _autoMirror);
        AddCheck(content, _autoAgent);
        AddCombo(content, "الحد الأقصى للأجهزة المتزامنة في Batch", _concurrency);

        var note = new Border
        {
            Background = Brush("#211D2A"),
            BorderBrush = Brush("#66516B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 14),
            Child = new TextBlock
            {
                Text = "هذه الإعدادات تتحكم في سلوك SoftBox المحلي فقط. إيقاف تثبيت Agent تلقائيًا لا يمنع التثبيت اليدوي عند طلبك، وإيقاف الاكتشاف التلقائي لا يمنع استخدام زر تحديث الأجهزة. لا يتم تخزين كلمات مرور أو مفاتيح.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#F0C9A5"),
                FontSize = 11
            }
        };
        content.Children.Add(note);

        var save = ButtonControl("حفظ وتطبيق الإعدادات", "#176044", Save_Click);
        save.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(save);
        _status.Text = "الإعدادات الحالية لم تتغير.";
        _status.Foreground = Brush("#9FC7E8");
        _status.Margin = new Thickness(0, 10, 0, 0);
        content.Children.Add(_status);

        root.Children.Add(content);
        return root;
    }

    private static void AddCheck(Panel panel, CheckBox checkBox)
    {
        checkBox.Foreground = Brush("#DCEBFA");
        checkBox.FontSize = 13;
        checkBox.Margin = new Thickness(0, 8, 0, 8);
        panel.Children.Add(checkBox);
    }

    private static void AddCombo(Panel panel, string label, ComboBox combo)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Brush("#9FC7E8"),
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 5)
        });
        combo.Height = 34;
        combo.Width = 200;
        combo.HorizontalAlignment = HorizontalAlignment.Left;
        combo.Background = Brush("#0A1B2D");
        combo.Foreground = Brush("#EAF3FF");
        combo.BorderBrush = Brush("#35516F");
        panel.Children.Add(combo);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new PhoneLabSettings(
                AutoDiscoverDevices: _autoDiscover.IsChecked == true,
                DiscoveryIntervalSeconds: _interval.SelectedItem is int interval ? interval : 3,
                AutoOpenScreenMirror: _autoMirror.IsChecked == true,
                AutoInstallAgent: _autoAgent.IsChecked == true,
                AutoInspectOnConnect: _autoInspect.IsChecked == true,
                MaximumBatchConcurrency: _concurrency.SelectedItem is int limit ? limit : 3);

            await _service.SaveAsync(settings);
            _apply(settings);
            _status.Text = "تم حفظ الإعدادات وتطبيقها.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر حفظ الإعدادات: " + ex.Message;
            MessageBox.Show(this, ex.Message, "إعدادات SoftBox",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            MinWidth = 170,
            Background = Brush(background),
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
