namespace SoftBox.PhoneLab.Core.Models;

public sealed class BoardMap
{
    public string Id { get; init; } = "generic-android";
    public string DisplayName { get; init; } = "Generic Android handset";
    public string Manufacturer { get; init; } = "";
    public string ModelPattern { get; init; } = "";
    public bool IsVerifiedForModel { get; init; }
    public string AccuracyNotice { get; init; } = "Generic educational schematic. Not an exact PCB map.";
    public List<BoardPart> Parts { get; init; } = [];
}

public sealed class BoardPart
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double Width { get; init; } = 1;
    public double Height { get; init; } = 1;
    public double Depth { get; init; } = 0.2;
    public string BaseColor { get; init; } = "#64748B";
}
