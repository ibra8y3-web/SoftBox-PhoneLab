using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class LockRecoveryDecisionEngineTests
{
    [Fact]
    public void AppleIos17UsesPreviousCredentialOnlyWhenKnownAndWithin72Hours()
    {
        var advice = LockRecoveryDecisionEngine.Evaluate(new LockRecoveryContext(
            "iOS", "Apple", "17.4", "", true, true, true, true, true, true));

        Assert.Equal(LockRecoveryRoute.PreviousCredential, advice.Route);
        Assert.False(advice.DataLossPossible);
        Assert.True(advice.RequiresPreviousCredential);
        Assert.Contains("105039", advice.OfficialUrl);
    }

    [Fact]
    public void SamsungOneUi61UsesPreviousCredentialOnlyWhenTheOwnerKnowsIt()
    {
        var advice = LockRecoveryDecisionEngine.Evaluate(new LockRecoveryContext(
            "Android", "Samsung", "14", "6.1", true, true, true, true, true, true));

        Assert.Equal(LockRecoveryRoute.PreviousCredential, advice.Route);
        Assert.True(advice.RequiresPreviousCredential);
        Assert.False(advice.DataLossPossible);
    }

    [Fact]
    public void SamsungWithoutPreviousCredentialDoesNotClaimUnlockIsPossible()
    {
        var advice = LockRecoveryDecisionEngine.Evaluate(new LockRecoveryContext(
            "Android", "Samsung", "14", "6.1", false, true, true, true, false, true));

        Assert.NotEqual(LockRecoveryRoute.PreviousCredential, advice.Route);
        Assert.Equal(LockRecoveryRoute.OfficialEraseAndRestore, advice.Route);
        Assert.True(advice.DataLossPossible);
    }

    [Fact]
    public void AndroidAccountRecoveryComesBeforeResetWhenAccountAccessIsMissing()
    {
        var advice = LockRecoveryDecisionEngine.Evaluate(new LockRecoveryContext(
            "Android", "Xiaomi", "14", "", false, false, false, true, false, true));

        Assert.Equal(LockRecoveryRoute.AccountRecovery, advice.Route);
        Assert.False(advice.DataLossPossible);
        Assert.Contains("accounts.google.com", advice.OfficialUrl);
    }

    [Fact]
    public void AndroidFindHubRouteWarnsAboutDataLossAndExistingGoogleAccount()
    {
        var advice = LockRecoveryDecisionEngine.Evaluate(new LockRecoveryContext(
            "Android", "OPPO", "15", "", false, false, true, true, true, true));

        Assert.Equal(LockRecoveryRoute.OfficialEraseAndRestore, advice.Route);
        Assert.True(advice.DataLossPossible);
        Assert.Contains("7663172", advice.OfficialUrl);
        Assert.Contains("حسابًا سبق مزامنته", advice.Explanation);
    }

    [Fact]
    public void OwnershipEvidenceIsRequiredBeforeAnyRecoveryRoute()
    {
        var advice = LockRecoveryDecisionEngine.Evaluate(new LockRecoveryContext(
            "Android", "Samsung", "15", "7.0", true, true, true, true, true, false));

        Assert.Equal(LockRecoveryRoute.OwnershipReview, advice.Route);
        Assert.False(advice.DataLossPossible);
    }

    [Fact]
    public void AndroidPreviousCodeOutside72HoursDoesNotTriggerPreviousCredentialRoute()
    {
        var advice = LockRecoveryDecisionEngine.Evaluate(new LockRecoveryContext(
            "Android", "Samsung", "15", "6.1", true, false, true, true, true, true));

        Assert.NotEqual(LockRecoveryRoute.PreviousCredential, advice.Route);
    }
}
