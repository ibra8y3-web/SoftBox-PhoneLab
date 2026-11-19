using System.IO;
using System.IO.Ports;
using System.Text.Json;

namespace SoftBox.PhoneLab.Desktop.Services;

public sealed record ExternalMeasurementSample(
    string Instrument, DateTimeOffset CapturedAtUtc,
    double? UsbVoltageV, double? BatteryVoltageV, double? BatteryCurrentA,
    double? BoardTemperatureC, double? UsbPowerW, Dictionary<string, double>? Rails);

/// <summary>Read-only JSON-lines serial bridge for authorized bench instruments.</summary>
public sealed class ExternalMeasurementClient : IDisposable
{
    private SerialPort? _port;
    public bool IsConnected => _port?.IsOpen == true;
    public string PortName => _port?.PortName ?? "";
    public static string[] ListPorts() => SerialPort.GetPortNames().OrderBy(x => x).ToArray();

    public void Connect(string portName, int baudRate = 115200)
    {
        if (!ListPorts().Contains(portName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Serial port is no longer available.");
        Disconnect();
        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            NewLine = "\n", ReadTimeout = 1800, WriteTimeout = 1800,
            DtrEnable = false, RtsEnable = false
        };
        port.Open();
        _port = port;
    }

    public ExternalMeasurementSample ReadSample()
    {
        var port = _port;
        if (port is null || !port.IsOpen) throw new InvalidOperationException("No measurement instrument is connected.");
        port.WriteLine("{\"command\":\"sample\",\"schema\":1}");
        var line = port.ReadLine().Trim();
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.TryGetProperty("schema", out var schema) && schema.GetInt32() != 1)
            throw new InvalidDataException("Unsupported measurement fixture protocol.");
        var rails = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("rails", out var railObject) && railObject.ValueKind == JsonValueKind.Object)
            foreach (var property in railObject.EnumerateObject())
                if (property.Value.TryGetDouble(out var value) && double.IsFinite(value))
                    rails[property.Name] = value;

        static double? Number(JsonElement e, string name)
            => e.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;

        return new ExternalMeasurementSample(
            root.TryGetProperty("instrument", out var instrument) ? instrument.GetString() ?? "USB fixture" : "USB fixture",
            DateTimeOffset.UtcNow, Number(root, "usbVoltageV"), Number(root, "batteryVoltageV"),
            Number(root, "batteryCurrentA"), Number(root, "boardTemperatureC"),
            Number(root, "usbPowerW"), rails);
    }

    public void Disconnect()
    {
        try { if (_port?.IsOpen == true) _port.Close(); }
        finally { _port?.Dispose(); _port = null; }
    }
    public void Dispose() => Disconnect();
}
