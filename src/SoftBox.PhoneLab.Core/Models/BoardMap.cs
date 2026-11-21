namespace SoftBox.PhoneLab.Core.Models;

public sealed class BoardMap
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = "generic-android";
    public string DisplayName { get; init; } = "Generic Android handset";
    public string Manufacturer { get; init; } = "";
    public string ModelPattern { get; init; } = "";
    public string[] ModelNumbers { get; init; } = [];
    public string[] DeviceCodes { get; init; } = [];
    public string HardwareRevision { get; init; } = "";
    public string BoardRevision { get; init; } = "";
    public bool ManualRevisionMatchConfirmed { get; init; }
    public string CoordinateUnit { get; init; } = "scene";
    public string CoordinateSystem { get; init; } = "center-origin";
    public double BoardWidthMm { get; init; }
    public double BoardHeightMm { get; init; }
    public string SourceName { get; init; } = "";
    public string SourceUri { get; init; } = "";
    public string SourceDocumentId { get; init; } = "";
    public string SourceLicense { get; init; } = "";
    public string SourceSha256 { get; init; } = "";
    public string VerifiedBy { get; init; } = "";
    public DateTimeOffset? VerifiedAtUtc { get; init; }
    public string AccuracyLevel { get; init; } = "generic";
    public bool IsVerifiedForModel { get; init; }
    public string AccuracyNotice { get; init; } = "Generic educational schematic. Not an exact PCB map.";
    public List<BoardMapLayer> Layers { get; init; } = [];
    public List<BoardNet> Nets { get; init; } = [];
    public List<BoardPart> Parts { get; init; } = [];
}

public sealed class BoardMapLayer
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Side { get; init; } = "Both";
    public bool VisibleByDefault { get; init; } = true;
}

public sealed class BoardNet
{
    public string Name { get; init; } = "";
    public string[] References { get; init; } = [];
}

public sealed class BoardPart
{
    public string Id { get; init; } = "";
    public string Reference { get; init; } = "";
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string Footprint { get; init; } = "";
    public string Side { get; init; } = "Both";
    public string Layer { get; init; } = "";
    public double RotationDegrees { get; init; }
    public string[] NetNames { get; init; } = [];
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double Width { get; init; } = 1;
    public double Height { get; init; } = 1;
    public double Depth { get; init; } = 0.2;
    public string BaseColor { get; init; } = "#64748B";
}
