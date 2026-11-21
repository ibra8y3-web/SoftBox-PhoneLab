using SoftBox.PhoneLab.Core.Compatibility;
using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceProfileCatalogValidatorTests
{
    [Fact]
    public void AcceptsCommunityCatalogWithExplicitSourceAndUnverifiedIdentity()
    {
        var result = DeviceProfileCatalogValidator.ValidateCommunityCatalog(ValidDocument());
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void RejectsNullOrMissingSourceIdentifierAndDigestWithoutThrowing()
    {
        var document = ValidDocument();
        var source = document.Sources[0];
        var invalid = new DeviceProfileCatalogDocument
        {
            SchemaVersion = 1,
            CatalogVersion = "1",
            Source = "test",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Sources =
            [
                new DeviceProfileSource
                {
                    Id = null!,
                    Name = source.Name,
                    Url = source.Url,
                    License = source.License,
                    Sha256 = null!,
                    Revision = source.Revision,
                    RecordCount = source.RecordCount,
                    RetrievedAtUtc = source.RetrievedAtUtc
                }
            ],
            Profiles = document.Profiles
        };

        var result = DeviceProfileCatalogValidator.ValidateCommunityCatalog(invalid);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void RejectsCommunityRecordThatClaimsOemVerifiedIdentity()
    {
        var document = ValidDocument();
        document = new DeviceProfileCatalogDocument
        {
            SchemaVersion = document.SchemaVersion,
            CatalogVersion = document.CatalogVersion,
            Source = document.Source,
            UpdatedAtUtc = document.UpdatedAtUtc,
            GeneratedAtUtc = document.GeneratedAtUtc,
            Sources = document.Sources,
            Profiles = [Copy(document.Profiles[0], identityVerified: true)]
        };

        var result = DeviceProfileCatalogValidator.ValidateCommunityCatalog(document);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("identityVerified=true", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsCommunityRecordThatClaimsAPcbMap()
    {
        var document = ValidDocument();
        document = new DeviceProfileCatalogDocument
        {
            SchemaVersion = 1,
            CatalogVersion = "1",
            Source = "test",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Sources = document.Sources,
            Profiles = [Copy(document.Profiles[0], boardMapId: "fake-board-map")]
        };

        var result = DeviceProfileCatalogValidator.ValidateCommunityCatalog(document);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("BoardMapId", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateModelCodesAreWarningsRatherThanSilentlyResolved()
    {
        var document = ValidDocument();
        var second = Copy(document.Profiles[0], id: "test-duplicate-profile");
        var updated = new DeviceProfileCatalogDocument
        {
            SchemaVersion = 1,
            CatalogVersion = "1",
            Source = "test",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Sources = document.Sources,
            Profiles = [document.Profiles[0], second]
        };

        var result = DeviceProfileCatalogValidator.ValidateCommunityCatalog(updated);
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
    }

    private static DeviceProfileCatalogDocument ValidDocument()
    {
        var now = DateTimeOffset.UtcNow;
        var source = new DeviceProfileSource
        {
            Id = "test-source",
            Name = "Test device identity source",
            Url = "https://example.com/catalog",
            License = "MIT",
            Sha256 = new string('a', 64),
            Revision = "abc123",
            RecordCount = 1,
            RetrievedAtUtc = now
        };
        return new DeviceProfileCatalogDocument
        {
            SchemaVersion = 1,
            CatalogVersion = "test",
            Source = "test",
            UpdatedAtUtc = now,
            GeneratedAtUtc = now,
            Sources = [source],
            Profiles =
            [
                new DeviceProfile
                {
                    Id = "test-device",
                    Manufacturer = "Test",
                    MarketingName = "Test Phone",
                    ModelNumbers = ["TST-100"],
                    DeviceCodes = ["test"],
                    IdentitySource = "Community",
                    IdentitySourceUrl = "https://example.com/device",
                    IdentitySourceLicense = "MIT",
                    IdentityVerified = false
                }
            ]
        };
    }

    private static DeviceProfile Copy(
        DeviceProfile source,
        string? id = null,
        bool? identityVerified = null,
        string? boardMapId = null)
        => new()
        {
            Id = id ?? source.Id,
            Manufacturer = source.Manufacturer,
            MarketingName = source.MarketingName,
            ModelNumbers = source.ModelNumbers,
            DeviceCodes = source.DeviceCodes,
            ModelAliases = source.ModelAliases,
            ProductNames = source.ProductNames,
            IdentitySource = source.IdentitySource,
            IdentitySourceUrl = source.IdentitySourceUrl,
            IdentitySourceLicense = source.IdentitySourceLicense,
            IdentityVerified = identityVerified ?? source.IdentityVerified,
            BoardMapId = boardMapId ?? source.BoardMapId
        };
}
