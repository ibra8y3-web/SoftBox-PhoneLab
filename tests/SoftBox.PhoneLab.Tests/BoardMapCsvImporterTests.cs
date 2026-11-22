using SoftBox.PhoneLab.Core.Models;
using SoftBox.PhoneLab.Desktop.Services;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class BoardMapCsvImporterTests
{
    private static readonly DeviceSnapshot TestDevice = new()
    {
        Manufacturer = "Samsung",
        Model = "Galaxy A52",
        ModelNumber = "SM-A525F",
        DeviceCode = "a52q",
        HardwareRevision = "REV1"
    };

    private static BoardMapCsvImportMetadata Metadata(
        double width = 70,
        double height = 145,
        string boardRevision = "REV1.0")
        => new(
            "Licensed BoardView export",
            "https://example.com/boardview/SM-A525F",
            "SM-A525F-board-rev1",
            "Licensed technician data",
            "Technician One",
            "REV1",
            boardRevision,
            width,
            height,
            "boardview",
            RevisionMatchConfirmed: true);

    private const string ValidCsv =
        "Reference,Name,Xmm,Ymm,WidthMm,HeightMm,Side,RotationDeg,Layer,Footprint,Value,Nets\r\n" +
        "U1,Application processor,32.5,65,14,14,Top,0,F.Cu,BGA,SM7125,VBAT|GND|VDD_CPU\r\n" +
        "C101,Decoupling capacitor,12,18.5,1,0.6,Bottom,90,B.Cu,0201,100nF,GND|VDD_CPU\r\n";

    [Fact]
    public void ImportsNormalizedCsvAndBuildsLayerAndNetIndexes()
    {
        var map = BoardMapCsvImporter.Parse(
            ValidCsv, new string('a', 64), TestDevice, Metadata());

        Assert.Equal(2, map.SchemaVersion);
        Assert.True(map.IsVerifiedForModel);
        Assert.True(map.ManualRevisionMatchConfirmed);
        Assert.Equal("mm", map.CoordinateUnit);
        Assert.Equal(2, map.Parts.Count);
        Assert.Equal("Top", map.Parts[0].Side);
        Assert.Equal("Bottom", map.Parts[1].Side);
        Assert.Equal(-0.45, map.Parts[1].Z);
        Assert.Equal(2, map.Layers.Count);
        Assert.Contains(map.Nets, net => net.Name == "VDD_CPU" && net.References.Contains("U1"));
        Assert.Matches("^[a-f0-9]{64}$", map.SourceSha256);
    }

    [Fact]
    public void RejectsDuplicateReferenceDesignators()
    {
        var csv = ValidCsv + "U1,Duplicate,40,40,2,2,Top,0,F.Cu,BGA,,\r\n";
        Assert.Throws<InvalidDataException>(() =>
            BoardMapCsvImporter.Parse(csv, new string('a', 64), TestDevice, Metadata()));
    }

    [Fact]
    public void RejectsComponentOutsideDeclaredPcbOutline()
    {
        var csv = ValidCsv.Replace("32.5,65,14,14", "69,65,14,14", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() =>
            BoardMapCsvImporter.Parse(csv, new string('a', 64), TestDevice, Metadata()));
    }

    [Fact]
    public void RejectsUnknownSideAndInvalidRotation()
    {
        var badSide = ValidCsv.Replace("Top,0,F.Cu", "Inside,0,F.Cu", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() =>
            BoardMapCsvImporter.Parse(badSide, new string('a', 64), TestDevice, Metadata()));

        var badRotation = ValidCsv.Replace("Top,0,F.Cu", "Top,360,F.Cu", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() =>
            BoardMapCsvImporter.Parse(badRotation, new string('a', 64), TestDevice, Metadata()));
    }

    [Fact]
    public void RequiresExplicitPhysicalRevisionConfirmationAndHttpsSource()
    {
        var unconfirmed = Metadata() with { RevisionMatchConfirmed = false };
        Assert.Throws<InvalidDataException>(() =>
            BoardMapCsvImporter.Parse(ValidCsv, new string('a', 64), TestDevice, unconfirmed));

        var insecure = Metadata() with { SourceUri = "http://example.com/map" };
        Assert.Throws<InvalidDataException>(() =>
            BoardMapCsvImporter.Parse(ValidCsv, new string('a', 64), TestDevice, insecure));
    }

    [Fact]
    public void RejectsUnknownModelIdentityAndOversizedBoardDimensions()
    {
        var unknown = TestDevice with { ModelNumber = "غير متاح", DeviceCode = "غير متاح" };
        Assert.Throws<InvalidDataException>(() =>
            BoardMapCsvImporter.Parse(ValidCsv, new string('a', 64), unknown, Metadata()));

        Assert.Throws<InvalidDataException>(() =>
            BoardMapCsvImporter.Parse(ValidCsv, new string('a', 64), TestDevice, Metadata(width: 800)));
    }

    [Fact]
    public void BoardCatalogRequiresExactSourceRevisionAndPhysicalMapConfirmation()
    {
        var map = BoardMapCsvImporter.Parse(
            ValidCsv, new string('a', 64), TestDevice, Metadata());
        Assert.True(BoardMapCatalog.IsVerifiedForDevice(map, TestDevice));

        var differentBoard = TestDevice with { BoardRevision = "REV2" };
        Assert.False(BoardMapCatalog.IsVerifiedForDevice(map, differentBoard));

        var noRevisionRead = TestDevice with { BoardRevision = "غير متاح" };
        Assert.True(BoardMapCatalog.IsVerifiedForDevice(map, noRevisionRead));

        var changedHardware = TestDevice with { HardwareRevision = "REV2" };
        Assert.False(BoardMapCatalog.IsVerifiedForDevice(map, changedHardware));

    }

    [Fact]
    public void RejectsMalformedQuotedCsv()
    {
        var malformed = ValidCsv + "R1,\"broken,50,50,1,1,Top,0,F.Cu,0402,1k,GND\r\n";
        Assert.Throws<InvalidDataException>(() =>
            BoardMapCsvImporter.Parse(malformed, new string('a', 64), TestDevice, Metadata()));
    }
}
