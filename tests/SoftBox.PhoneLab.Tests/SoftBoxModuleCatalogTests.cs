using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class SoftBoxModuleCatalogTests
{
    [Fact]
    public void MasterCatalogContainsAllThirtySixSectionsInSpecificationOrder()
    {
        Assert.Equal(SoftBoxModuleCatalog.ExpectedModuleCount, SoftBoxModuleCatalog.All.Count);
        Assert.Equal(Enumerable.Range(1, SoftBoxModuleCatalog.ExpectedModuleCount),
            SoftBoxModuleCatalog.All.Select(x => x.Order));
    }

    [Fact]
    public void ModuleIdentifiersAreUniqueAndStable()
    {
        var ids = SoftBoxModuleCatalog.All.Select(x => x.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ids, id => Assert.Matches("^[a-z0-9-]+$", id));
    }

    [Fact]
    public void EachModuleHasANameSummaryAndAtLeastOneAuditedFeature()
    {
        Assert.All(SoftBoxModuleCatalog.All, module =>
        {
            Assert.False(string.IsNullOrWhiteSpace(module.NameAr));
            Assert.False(string.IsNullOrWhiteSpace(module.NameEn));
            Assert.False(string.IsNullOrWhiteSpace(module.SummaryAr));
            Assert.NotEmpty(module.Features);
            Assert.All(module.Features, feature =>
            {
                Assert.False(string.IsNullOrWhiteSpace(feature.NameAr));
                Assert.False(string.IsNullOrWhiteSpace(feature.DetailsAr));
            });
        });
    }

    [Fact]
    public void FeatureIdsAreUniqueInsideEachModule()
    {
        foreach (var module in SoftBoxModuleCatalog.All)
        {
            var ids = module.Features.Select(x => x.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public void EveryNavigationActionHasAnExplicitSupportedActionIdentifier()
    {
        var actions = SoftBoxModuleCatalog.All
            .SelectMany(x => x.Features)
            .Where(x => x.ActionId is not null)
            .Select(x => x.ActionId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.Contains(action, SoftBoxModuleCatalog.SupportedActionIds));
    }

    [Fact]
    public void ExistingToolsHaveOneCanonicalNavigationEntry()
    {
        var actions = SoftBoxModuleCatalog.All
            .SelectMany(x => x.Features)
            .Where(x => x.ActionId is not null)
            .Select(x => x.ActionId!)
            .ToList();

        Assert.Equal(actions.Count, actions.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            SoftBoxModuleCatalog.SupportedActionIds.OrderBy(x => x, StringComparer.Ordinal),
            actions.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void PlannedAndUnsupportedFeaturesNeverExposeRunButtons()
    {
        var unfinished = SoftBoxModuleCatalog.All.SelectMany(x => x.Features)
            .Where(x => x.Status is ModuleImplementationStatus.Planned or ModuleImplementationStatus.Unsupported);

        Assert.All(unfinished, feature => Assert.Null(feature.ActionId));
    }
}
