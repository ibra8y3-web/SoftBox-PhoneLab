using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Threading;
using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Services;

namespace SoftBox.PhoneLab.Desktop;

public partial class MeasurementWindow : Window
{
    private readonly ExternalMeasurementClient _client = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly List<ExternalMeasurementSample> _history = [];
    private bool _reading;
    public event Action<ExternalMeasurementSample>? SampleReceived;

    public MeasurementWindow()
    {
        InitializeComponent();
        RefreshPorts();
        SampleLog.ItemsSource = SampleLines;
        _timer.Tick += async (_, _) => await ReadSampleAsync();
        Closed += (_, _) => { _timer.Stop(); _client.Dispose(); };
    }

    public ObservableCollection<string> SampleLines { get; } = [];

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
            _history.Add(sample);
            InstrumentName.Text = sample.Instrument;
            UsbVoltage.Text = Format(sample.UsbVoltageV, "V");
            BatteryVoltage.Text = Format(sample.BatteryVoltageV, "V");
            BatteryCurrent.Text = Format(sample.BatteryCurrentA, "A");
            BoardTemperature.Text = Format(sample.BoardTemperatureC, "°C");
            BatteryCapacity.Text = Format(sample.BatteryCapacityMah, "mAh");
            BatteryResistance.Text = Format(sample.BatteryInternalResistanceMilliOhm, "mΩ");
            PowerValue.Text = "USB Power: " + Format(sample.UsbPowerW, "W");
            RailsValue.Text = sample.Rails is { Count: > 0 }
                ? string.Join("   •   ", sample.Rails.Select(x => $"{x.Key}: {x.Value:0.000} V"))
                : "لم تصل قراءات خطوط الطاقة بعد.";
            SampleLines.Insert(0, $"{sample.CapturedAtUtc.LocalDateTime:T} | USB {Format(sample.UsbVoltageV, "V")} | BAT {Format(sample.BatteryVoltageV, "V")} | I {Format(sample.BatteryCurrentA, "A")}");
            while (SampleLines.Count > 100) SampleLines.RemoveAt(SampleLines.Count - 1);
            StatusText.Text = $"آخر عينة: {sample.CapturedAtUtc.LocalDateTime:T} • {_client.PortName} • {_history.Count} عينة";
            SampleReceived?.Invoke(sample);
        }
        catch (TimeoutException) { StatusText.Text = "متصل، لكن لم تصل عينة ضمن المهلة. تحقق من بروتوكول جهاز القياس."; }
        catch (Exception ex) { StatusText.Text = "تعذر قراءة الجهاز: " + ex.Message; _timer.Stop(); }
        finally { _reading = false; }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0) { StatusText.Text = "لا توجد عينات لحفظها بعد."; return; }
        var dialog = new SaveFileDialog { Filter = "CSV file|*.csv", FileName = "PhoneLab-measurements.csv" };
        if (dialog.ShowDialog(this) != true) return;
        static string Cell(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        var text = new StringBuilder("timestampUtc,instrument,usbVoltageV,batteryVoltageV,batteryCurrentA,boardTemperatureC,usbPowerW,batteryCapacityMah,batteryInternalResistanceMilliOhm,railsJson\r\n");
        foreach (var item in _history)
            text.Append(Cell(item.CapturedAtUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Cell(item.Instrument)).Append(',')
                .Append(item.UsbVoltageV?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(item.BatteryVoltageV?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(item.BatteryCurrentA?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(item.BoardTemperatureC?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(item.UsbPowerW?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(item.BatteryCapacityMah?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(item.BatteryInternalResistanceMilliOhm?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(Cell(System.Text.Json.JsonSerializer.Serialize(item.Rails))).Append("\r\n");
        File.WriteAllText(dialog.FileName, text.ToString(), new UTF8Encoding(true));
        StatusText.Text = $"تم حفظ {_history.Count} عينة قياس في {dialog.FileName}";
    }

    private static string Format(double? value, string unit)
        => value.HasValue ? $"{value.Value:0.000} {unit}" : $"— {unit}";
}
