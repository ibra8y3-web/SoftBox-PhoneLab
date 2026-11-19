namespace SoftBox.PhoneLab.Core.Models;

public sealed record ExternalMeasurementSample(
    string Instrument,
    DateTimeOffset CapturedAtUtc,
    double? UsbVoltageV,
    double? BatteryVoltageV,
    double? BatteryCurrentA,
    double? BoardTemperatureC,
    double? UsbPowerW,
    Dictionary<string, double>? Rails,
    double? BatteryCapacityMah = null,
    double? BatteryInternalResistanceMilliOhm = null);
