using SoftBox.PhoneLab.Core.Compatibility;
using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class DeviceProfileCatalogMergerTests
{
    [Fact]
    public void MergesSameManufacturerModelAndPreservesCuratedTrust()
    {
        var curated = Profile("curated-a52", "Samsung", ["SM-A525F"], ["a52q"], verified: true);
        var community = Profile("community-a52", "Samsung", ["sm a525f"], ["a52q"],
            verified: false, cpu: "Snapdragon 720G", capacity: 4500);

        var merged = DeviceProfileCatalogMerger.Merge([curated], [community]);

        var result = Assert.Single(merged);
        Assert.Equal("curated-a52", result.Id);
        Assert.True(result.IdentityVerified);
        Assert.Contains("a52q", result.DeviceCodes);
        Assert.Equal("Snapdragon 720G", result.Cpu);
        Assert.Equal(4500, result.DesignBatteryCapacityMah);
    }

    [Fact]
    public void DoesNotMergeSameModelCodeAcrossDifferentManufacturers()
    {
        var samsung = Profile("samsung", "Samsung", ["SM-123"], [], verified: true);
        var other = Profile("other", "Other", ["SM-123"], [], verified: false);

        var merged = DeviceProfileCatalogMerger.Merge([samsung], [other]);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, item => item.Manufacturer == "Samsung");
        Assert.Contains(merged, item => item.Manufacturer == "Other");
    }

    [Fact]
    public void RetainsAmbiguousSameManufacturerIdentifierInsteadOfMergingIntoFirstMatch()
    {
        var first = Profile("first", "Example", ["EX-100"], ["shared"], verified: false);
        var second = Profile("second", "Example", ["EX-200"], ["shared"], verified: false);
        var incoming = Profile("incoming", "Example", ["EX-100"], ["shared"], verified: false);

        var merged = DeviceProfileCatalogMerger.Merge([first, second], [incoming]);

        Assert.Equal(3, merged.Count);
        Assert.Contains(merged, item => item.Id == "incoming");
    }

    [Fact]
    public void IgnoresSameProfileIdWhenIdentifiersContradict()
    {
        var first = Profile("same-id", "Example", ["EX-100"], [], verified: false);
        var conflicting = Profile("same-id", "Example", ["EX-200"], [], verified: false);

        var merged = DeviceProfileCatalogMerger.Merge([first], [conflicting]);

        Assert.Single(merged);
        Assert.Equal("EX-100", Assert.Single(merged[0].ModelNumbers));
    }

    private static DeviceProfile Profile(string id, string manufacturer, string[] models,
        string[] codes, bool verified, string cpu = "", int? capacity = null)
        => new()
        {
            Id = id,
            Manufacturer = manufacturer,
            MarketingName = id,
            ModelNumbers = models,
            DeviceCodes = codes,
            IdentitySource = "test",
            IdentitySourceUrl = "https://example.com/device",
            IdentitySourceLicense = "MIT",
            IdentityVerified = verified,
            Cpu = cpu,
            DesignBatteryCapacityMah = capacity
        };
}
