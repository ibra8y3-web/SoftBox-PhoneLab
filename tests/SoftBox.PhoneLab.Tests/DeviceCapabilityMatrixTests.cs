using SoftBox.PhoneLab.Core.Compatibility;
using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceCapabilityMatrixTests
{
    [Fact]
    public void MatrixReportsActualProfileAndBoardEvidenceWithoutInferringMissingBoards()
    {
        var catalog = new DeviceProfileCatalogDocument
        {
            CatalogVersion = "test-1",
            Profiles =
            [
                new DeviceProfile
                {
                    Id = "model-with-board-data",
                    Manufacturer = "Example",
                    MarketingName = "Example Phone",
                    ModelNumbers = ["X100"],
                    DeviceCodes = ["example"],
                    ProductNames = ["example"],
                    BoardIds = ["board-rev-a"],
                    SocAliases = ["SOC-1"],
                    HardwareFamily = "SOC-1",
                    BoardMapId = "example-rev-a",
                    IdentitySource = "Manufacturer documentation",
                    IdentitySourceUrl = "https://example.com/device",
                    IdentityVerified = true
                },
                new DeviceProfile
                {
                    Id = "identity-only",
                    Manufacturer = "Example",
                    MarketingName = "Unknown board revision",
                    ModelNumbers = ["X200"],
                    DeviceCodes = ["example2"],
                    ProductNames = ["example2"],
                    SocAliases = ["SOC-2"],
                    HardwareFamily = "SOC-2",
                    IdentitySource = "Community mapping",
                    IdentitySourceUrl = "http://example.invalid/device",
                    IdentityVerified = false
                }
            ]
        };

        var report = DeviceCapabilityMatrixBuilder.Build(
            catalog, DateTimeOffset.Parse("2026-11-21T00:00:00Z"));

        Assert.Equal(2, report.TotalProfiles);
        Assert.Equal(2, report.ProfilesWithModelNumbers);
        Assert.Equal(1, report.ProfilesWithBoardIds);
        Assert.Equal(1, report.ProfilesWithBoardMapReferences);
        Assert.Equal(1, report.ProfilesWithHttpsIdentitySources);
        Assert.Equal(1, report.ProfilesMarkedIdentityVerified);
        Assert.Equal(1, report.ProfilesMissingBoardEvidence);

        var incomplete = Assert.Single(report.Profiles, item => item.ProfileId == "identity-only");
        Assert.Equal(0, incomplete.BoardIdCount);
        Assert.False(incomplete.IdentitySourceIsHttps);
        Assert.Contains("Board ID / board revision evidence", incomplete.MissingEvidence);
        Assert.Contains("Board-map catalogue reference", incomplete.MissingEvidence);
        Assert.Equal("Identity profile only; board evidence incomplete", incomplete.CoverageLevel);
    }

    [Fact]
    public void CsvExportEscapesFieldsAndNeutralizesSpreadsheetFormulas()
    {
        var report = DeviceCapabilityMatrixBuilder.Build(new DeviceProfileCatalogDocument
        {
            Profiles =
            [
                new DeviceProfile
                {
                    Id = "safe-id",
                    Manufacturer = "=HYPERLINK(\"https://evil.example\")",
                    MarketingName = "Device, Test",
                    IdentitySourceUrl = "https://example.com"
                }
            ]
        });

        var csv = DeviceCapabilityMatrixBuilder.ToCsv(report);

        Assert.Contains("\"'=HYPERLINK(\"\"https://evil.example\"\")\"", csv);
        Assert.Contains("\"Device, Test\"", csv);
    }
}
