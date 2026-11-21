using SoftBox.PhoneLab.Core.Recovery;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class OfficialRecoveryCaseServiceTests
{
    [Fact]
    public void CatalogContainsExactlyTheSixRequestedOfficialRecoveryTracks()
    {
        Assert.Equal(6, OfficialRecoveryWorkflowCatalog.All.Count);
        Assert.Equal(
            Enum.GetValues<OfficialRecoveryCaseKind>().OrderBy(value => value),
            OfficialRecoveryWorkflowCatalog.All.Select(workflow => workflow.Kind).OrderBy(value => value));
        Assert.All(OfficialRecoveryWorkflowCatalog.All, workflow =>
        {
            Assert.NotEmpty(workflow.Steps);
            Assert.All(workflow.Resources, resource =>
            {
                Assert.True(Uri.TryCreate(resource.Url, UriKind.Absolute, out var uri));
                Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            });
        });
    }

    [Theory]
    [InlineData(OfficialRecoveryCaseKind.GoogleFrp)]
    [InlineData(OfficialRecoveryCaseKind.XiaomiMiAccount)]
    [InlineData(OfficialRecoveryCaseKind.SamsungFrp)]
    [InlineData(OfficialRecoveryCaseKind.ScreenLock)]
    [InlineData(OfficialRecoveryCaseKind.MdmRelease)]
    [InlineData(OfficialRecoveryCaseKind.CarrierNetworkUnlock)]
    public void BuildsARealLocalPlanWhenTheRequiredAuthorityIsConfirmed(OfficialRecoveryCaseKind kind)
    {
        var input = ValidInput() with
        {
            OrganizationAdministratorAuthorized = kind == OfficialRecoveryCaseKind.MdmRelease,
            CarrierAccountHolderAuthorized = kind == OfficialRecoveryCaseKind.CarrierNetworkUnlock
        };

        var result = OfficialRecoveryCaseService.Build(
            kind,
            input,
            new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));

        Assert.True(result.ReadyForOfficialContact);
        Assert.Empty(result.ValidationErrors);
        Assert.StartsWith("SBX-20261121-120000-", result.CaseReference);
        Assert.Contains(OfficialRecoveryWorkflowCatalog.Get(kind).TitleAr, result.Markdown);
        Assert.Contains("This file is a locally generated checklist", result.Markdown);
        Assert.Contains("No data is erased", result.Markdown);
    }

    [Fact]
    public void RefusesToBuildAPlanWithoutOwnerOrRepairAuthorization()
    {
        var result = OfficialRecoveryCaseService.Build(
            OfficialRecoveryCaseKind.GoogleFrp,
            ValidInput() with { OwnerOrAuthorized = false });

        Assert.False(result.ReadyForOfficialContact);
        Assert.Empty(result.Markdown);
        Assert.Contains(result.ValidationErrors, error => error.Contains("تفويض"));
    }

    [Fact]
    public void MdmReleaseRequiresAnAuthorizedOrganizationAdministrator()
    {
        var result = OfficialRecoveryCaseService.Build(
            OfficialRecoveryCaseKind.MdmRelease,
            ValidInput());

        Assert.False(result.ReadyForOfficialContact);
        Assert.Contains(result.ValidationErrors, error => error.Contains("مسؤول المؤسسة"));
    }

    [Fact]
    public void CarrierUnlockRequiresTheCarrierAccountHolderOrDelegate()
    {
        var result = OfficialRecoveryCaseService.Build(
            OfficialRecoveryCaseKind.CarrierNetworkUnlock,
            ValidInput());

        Assert.False(result.ReadyForOfficialContact);
        Assert.Contains(result.ValidationErrors, error => error.Contains("صاحب حساب المشغّل"));
    }

    [Fact]
    public void ManufacturerAndModelAreRequired()
    {
        var result = OfficialRecoveryCaseService.Build(
            OfficialRecoveryCaseKind.SamsungFrp,
            ValidInput() with { Model = "   " });

        Assert.False(result.ReadyForOfficialContact);
        Assert.Contains(result.ValidationErrors, error => error.Contains("الموديل"));
    }

    [Fact]
    public void ExportDoesNotCollectOrIncludeDeviceOrAccountSecrets()
    {
        var result = OfficialRecoveryCaseService.Build(
            OfficialRecoveryCaseKind.CarrierNetworkUnlock,
            ValidInput() with { CarrierAccountHolderAuthorized = true });

        Assert.True(result.ReadyForOfficialContact);
        Assert.DoesNotContain("356789012345678", result.Markdown);
        Assert.DoesNotContain("password123", result.Markdown);
        Assert.DoesNotContain("123456", result.Markdown);
        Assert.Contains("IMEI", result.Markdown);
        Assert.Contains("only through its official secure channel", result.Markdown);
    }

    private static OfficialRecoveryCaseInput ValidInput()
        => new(
            "Samsung",
            "SM-A315G",
            OwnerOrAuthorized: true,
            ProofOfOwnershipAvailable: true,
            OrganizationAdministratorAuthorized: false,
            CarrierAccountHolderAuthorized: false);
}
