using System.Windows.Media;

namespace SoftBox.PhoneLab.Desktop.Models;

public sealed record DeviceListItem(string Serial, string State, string Manufacturer, string Model)
{
    public bool IsAuthorized => State.Equals("device", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => !string.IsNullOrWhiteSpace(Model) && Model != "غير متاح"
        ? $"{Manufacturer} {Model}".Trim()
        : IsAuthorized ? "Android phone" : "Android device";
    public string MaskedSerial => Serial.Length > 4 ? "••••" + Serial[^4..] : "••••";
    public string StateText => IsAuthorized ? "متصل ومصرّح" :
        State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase) ? "بانتظار موافقة الهاتف" : State;
    public Brush StateBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(
        IsAuthorized ? "#38BDF8" : State.Equals("unauthorized", StringComparison.OrdinalIgnoreCase) ? "#F59E0B" : "#64748B"));
}

public sealed record PropertyLine(string Label, string Value);
