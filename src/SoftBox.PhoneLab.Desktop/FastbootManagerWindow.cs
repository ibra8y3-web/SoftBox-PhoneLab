using SoftBox.PhoneLab.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop;

/// <summary>
/// Fastboot device discovery and read-only bootloader variable inspection.
/// The only state-changing action is a user-confirmed reboot to Android system.
/// </summary>
public sealed class FastbootManagerWindow : Window
{
    private readonly FastbootService _fastboot = new();
    private readonly ObservableCollection<FastbootDevice> _devices = [];
    private readonly ListBox _deviceList = new();
    private readonly TextBox _details = new();
    private readonly TextBlock _status = new();
    private readonly Button _refreshButton;
    private readonly Button _inspectButton;
    private readonly Button _rebootButton;
    private CancellationTokenSource? _operation;

    public FastbootManagerWindow()
    {
        Title = "SoftBox | Fastboot Manager";
        Width = 1000;
        Height = 740;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brush("#081321");
        Foreground = Brush("#EAF3FF");

        _refreshButton = ButtonControl("اكتشاف أجهزة Fastboot", "#14517A", Refresh_Click);
        _inspectButton = ButtonControl("قراءة متغيرات الجهاز", "#176044", Inspect_Click);
        _rebootButton = ButtonControl("إعادة تشغيل Android", "#704B20", Reboot_Click);
        _inspectButton.IsEnabled = false;
        _rebootButton.IsEnabled = false;
        Content = BuildContent();
        _deviceList.SelectionChanged += (_, _) =>
        {
            _inspectButton.IsEnabled = _deviceList.SelectedItem is FastbootDevice && _operation is null;
            _rebootButton.IsEnabled = _deviceList.SelectedItem is FastbootDevice && _operation is null;
        };
        Loaded += async (_, _) => await RefreshDevicesAsync();
        Closed += (_, _) => _operation?.Cancel();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Fastboot Manager",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#7DD3FC")
        });
        header.Children.Add(new TextBlock
        {
            Text = "FASTBOOT DEVICE DISCOVERY • READ-ONLY BOOTLOADER INSPECTION",
            FontSize = 10,
            Foreground = Brush("#8199B5"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = "يتطلب fastboot.exe من Android SDK Platform-Tools. هذه الوحدة تكتشف الأجهزة وتقرأ getvar all فقط. لا توجد أوامر Flash أو Unlock أو Erase أو Format أو Boot. إعادة التشغيل إلى Android تتطلب تأكيدك.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#F0C9A5"),
            Background = Brush("#211D2A"),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(_refreshButton);
        actions.Children.Add(_inspectButton);
        actions.Children.Add(_rebootButton);
        DockPanel.SetDock(actions, Dock.Top);
        root.Children.Add(actions);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        _deviceList.ItemsSource = _devices;
        _deviceList.DisplayMemberPath = nameof(FastbootDevice.DisplayLabel);
        _deviceList.Background = Brush("#071625");
        _deviceList.Foreground = Brush("#DCEBFA");
        _deviceList.BorderBrush = Brush("#263D58");
        _deviceList.Padding = new Thickness(8);
        _deviceList.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(_deviceList, 0);
        body.Children.Add(_deviceList);

        _details.IsReadOnly = true;
        _details.AcceptsReturn = true;
        _details.TextWrapping = TextWrapping.Wrap;
        _details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _details.Background = Brush("#071625");
        _details.Foreground = Brush("#DCEBFA");
        _details.BorderBrush = Brush("#263D58");
        _details.Padding = new Thickness(10);
        _details.FontFamily = new FontFamily("Consolas");
        _details.FontSize = 10;
        _details.Text = "اضغط اكتشاف أجهزة Fastboot. يجب أن يكون الهاتف موصولًا في وضع Bootloader/Fastboot.";
        Grid.SetColumn(_details, 1);
        body.Children.Add(_details);
        root.Children.Add(body);

        _status.Text = "جارٍ التحقق من توفر fastboot.exe...";
        _status.Foreground = Brush("#9FC7E8");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        return root;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await RefreshDevicesAsync();

    private async Task RefreshDevicesAsync()
    {
        if (_operation is not null)
            return;
        if (!_fastboot.IsAvailable)
        {
            _status.Text = "Fastboot غير متاح. لم يتم العثور على fastboot.exe في Tools/platform-tools أو PATH.";
            _details.Text = _status.Text;
            return;
        }

        await RunOperationAsync("جارٍ البحث عن أجهزة Fastboot المتصلة...", async token =>
        {
            var devices = await _fastboot.DiscoverAsync(token);
            _devices.Clear();
            foreach (var device in devices)
                _devices.Add(device);
            _deviceList.SelectedIndex = _devices.Count == 1 ? 0 : -1;
            _details.Text = _devices.Count == 0
                ? "لم يتم العثور على جهاز Fastboot. تحقق من وضع Bootloader وكابل USB والتعريف."
                : string.Join(Environment.NewLine, _devices.Select(device => device.DisplayLabel));
            return $"تم اكتشاف {_devices.Count:N0} جهاز Fastboot.";
        });
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e)
    {
        if (_deviceList.SelectedItem is not FastbootDevice selected)
        {
            _status.Text = "اختر جهاز Fastboot أولًا.";
            return;
        }

        await RunOperationAsync("جارٍ قراءة متغيرات Bootloader من الجهاز المحدد...", async token =>
        {
            var output = await _fastboot.ReadAllVariablesAsync(selected.Serial, token);
            _details.Text =
                $"FASTBOOT DEVICE: {selected.Serial}\n" +
                $"DETAILS: {selected.Details}\n" +
                "COMMAND: fastboot -s <selected-serial> getvar all\n" +
                "MODE: READ ONLY\n\n" + output +
                "\n\nهذه القيم قرأها Fastboot من الجهاز. لا تعني وحدها إثبات توافق Firmware أو إمكانية فتح Bootloader.";
            return "اكتملت قراءة متغيرات Fastboot. لم يتم تغيير أي قسم أو إعداد في الجهاز.";
        });
    }

    private async void Reboot_Click(object sender, RoutedEventArgs e)
    {
        if (_deviceList.SelectedItem is not FastbootDevice selected)
        {
            _status.Text = "اختر جهاز Fastboot أولًا.";
            return;
        }

        var confirmation = MessageBox.Show(this,
            $"إرسال أمر fastboot reboot إلى الجهاز المحدد:\n{selected.Serial}\n\nسيخرج الجهاز من وضع Fastboot ويحاول تشغيل Android. متابعة؟",
            "تأكيد إعادة التشغيل", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await RunOperationAsync("جارٍ إرسال أمر إعادة التشغيل إلى Android...", async token =>
        {
            var output = await _fastboot.RebootSystemAsync(selected.Serial, token);
            _devices.Remove(selected);
            _inspectButton.IsEnabled = false;
            _rebootButton.IsEnabled = false;
            _details.Text = "تم إرسال أمر إعادة التشغيل إلى الجهاز.\n" + output +
                            "\nقد يختفي الجهاز من قائمة Fastboot أثناء انتقاله إلى Android.";
            return "أُرسل أمر إعادة التشغيل إلى Android. أعد اكتشاف الجهاز بعد اكتمال إقلاعه.";
        });
    }

    private async Task RunOperationAsync(string message, Func<CancellationToken, Task<string>> operation)
    {
        if (_operation is not null)
            return;

        _operation = new CancellationTokenSource();
        SetBusy(true, message);
        try
        {
            _status.Text = await operation(_operation.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "تم إلغاء العملية.";
        }
        catch (Exception ex)
        {
            _status.Text = "فشلت عملية Fastboot: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Fastboot Manager",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false, _status.Text);
        }
    }

    private void SetBusy(bool busy, string message)
    {
        _status.Text = message;
        _refreshButton.IsEnabled = !busy;
        _inspectButton.IsEnabled = !busy && _deviceList.SelectedItem is FastbootDevice;
        _rebootButton.IsEnabled = !busy && _deviceList.SelectedItem is FastbootDevice;
    }

    private static Button ButtonControl(string label, string background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 7, 0),
            MinWidth = 140,
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
