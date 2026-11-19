using System.Windows;
using System.Windows.Threading;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

public partial class MeasurementWindow : Window
{
    private readonly ExternalMeasurementClient _client = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _reading;

    public MeasurementWindow()
    {
        InitializeComponent();
        RefreshPorts();
        _timer.Tick += async (_, _) => await ReadSampleAsync();
        Closed += (_, _) => { _timer.Stop(); _client.Dispose(); };
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void RefreshPorts()
    {
        Ports.ItemsSource = ExternalMeasurementClient.ListPorts();
        if (Ports.Items.Count > 0 && Ports.SelectedIndex < 0) Ports.SelectedIndex = 0;
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_client.IsConnected)
        {
            _timer.Stop();
            _client.Disconnect();
            ConnectButton.Content = "اتصال";
            StatusText.Text = "تم فصل جهاز القياس.";
            InstrumentName.Text = "لا يوجد جهاز قياس متصل";
            return;
        }

        if (Ports.SelectedItem is not string port)
        {
            StatusText.Text = "لا توجد منافذ COM متاحة.";
            return;
        }

        try
        {
            _client.Connect(port);
            ConnectButton.Content = "فصل";
            StatusText.Text = $"متصل على {port}. انتظار أول عينة JSON.";
            _timer.Start();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task ReadSampleAsync()
    {
        if (_reading || !_client.IsConnected) return;
        _reading = true;
        try
        {
            var sample = await Task.Run(_client.ReadSample);
            InstrumentName.Text = sample.Instrument;
            UsbVoltage.Text = Format(sample.UsbVoltageV, "V");
            BatteryVoltage.Text = Format(sample.BatteryVoltageV, "V");
            BatteryCurrent.Text = Format(sample.BatteryCurrentA, "A");
            BoardTemperature.Text = Format(sample.BoardTemperatureC, "°C");
            PowerValue.Text = "USB Power: " + Format(sample.UsbPowerW, "W");
            RailsValue.Text = sample.Rails is { Count: > 0 }
                ? string.Join("   •   ", sample.Rails.Select(x => $"{x.Key}: {x.Value:0.000} V"))
                : "لم تصل قراءات خطوط الطاقة بعد.";
            StatusText.Text = $"آخر عينة: {sample.CapturedAtUtc.LocalDateTime:T} • {_client.PortName}";
        }
        catch (TimeoutException) { StatusText.Text = "متصل، لكن لم تصل عينة ضمن المهلة. تحقق من بروتوكول جهاز القياس."; }
        catch (Exception ex) { StatusText.Text = "تعذر قراءة الجهاز: " + ex.Message; _timer.Stop(); }
        finally { _reading = false; }
    }

    private static string Format(double? value, string unit)
        => value.HasValue ? $"{value.Value:0.000} {unit}" : $"— {unit}";
}
