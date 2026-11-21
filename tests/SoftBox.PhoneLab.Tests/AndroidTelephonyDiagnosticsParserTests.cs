using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class AndroidTelephonyDiagnosticsParserTests
{
    [Fact]
    public void ParsesVoiceAndDataRegistrationAndRadioTechnologies()
    {
        const string output =
            "mServiceState={mVoiceRegState=0(IN_SERVICE), mDataRegState=0(IN_SERVICE), " +
            "mRilVoiceRadioTechnology=14(LTE), mRilDataRadioTechnology=20(NR), " +
            "mIsRoaming=false, mIsEmergencyOnly=false, mIsSearching=false, " +
            "mOperatorAlphaLong=Example Network, mOperatorAlphaShort=Example}\n";

        var report = AndroidTelephonyDiagnosticsParser.Parse(output);

        Assert.True(report.OutputReadable);
        Assert.Equal(AndroidNetworkRegistrationState.InService, report.VoiceRegistration);
        Assert.Equal(AndroidNetworkRegistrationState.InService, report.DataRegistration);
        Assert.Equal("LTE", report.VoiceRadioTechnology);
        Assert.Equal("NR", report.DataRadioTechnology);
        Assert.False(report.IsRoaming);
        Assert.False(report.IsEmergencyOnly);
        Assert.False(report.IsSearching);
        Assert.Equal("Example Network", report.OperatorName);
    }

    [Theory]
    [InlineData("0(IN_SERVICE)", AndroidNetworkRegistrationState.InService)]
    [InlineData("1(OUT_OF_SERVICE)", AndroidNetworkRegistrationState.OutOfService)]
    [InlineData("2(EMERGENCY_ONLY)", AndroidNetworkRegistrationState.EmergencyOnly)]
    [InlineData("3(POWER_OFF)", AndroidNetworkRegistrationState.PowerOff)]
    [InlineData("7(UNKNOWN_STATE)", AndroidNetworkRegistrationState.Unknown)]
    public void ParsesOnlyKnownRegistrationStates(string value, AndroidNetworkRegistrationState expected)
    {
        var report = AndroidTelephonyDiagnosticsParser.Parse("mVoiceRegState=" + value + ",");
        Assert.Equal(expected, report.VoiceRegistration);
    }

    [Fact]
    public void ConflictingRegistrationAndBooleanValuesRemainUnknown()
    {
        var report = AndroidTelephonyDiagnosticsParser.Parse(
            "mVoiceRegState=0(IN_SERVICE), mVoiceRegState=1(OUT_OF_SERVICE), " +
            "mIsRoaming=true, mIsRoaming=false");

        Assert.Equal(AndroidNetworkRegistrationState.Unknown, report.VoiceRegistration);
        Assert.Null(report.IsRoaming);
    }

    [Fact]
    public void MissingAndPermissionDeniedFieldsRemainUnknown()
    {
        var missing = AndroidTelephonyDiagnosticsParser.Parse("some unrelated telephony output");
        var denied = AndroidTelephonyDiagnosticsParser.Parse("Permission Denial: can't dump telephony.registry");

        Assert.True(missing.OutputReadable);
        Assert.Equal(AndroidNetworkRegistrationState.Unknown, missing.VoiceRegistration);
        Assert.Equal("Unknown", missing.DataRadioTechnology);
        Assert.Null(missing.IsRoaming);
        Assert.Empty(missing.OperatorName);

        Assert.False(denied.OutputReadable);
        Assert.Equal(AndroidNetworkRegistrationState.Unknown, denied.DataRegistration);
    }

    [Fact]
    public void CapturesOnlyAllowListedRadioFieldsAndDoesNotExposeSubscriberIdentifiers()
    {
        const string output =
            "mVoiceRegState=0(IN_SERVICE), mDataRegState=1(OUT_OF_SERVICE), " +
            "mRilVoiceRadioTechnology=14(LTE), mRilDataRadioTechnology=13(EHRPD), " +
            "mIsRoaming=true, mOperatorAlphaLong=Example, " +
            "mImei=123456789012345, mImsi=001010123456789, mIccId=89012345678901234567";

        var report = AndroidTelephonyDiagnosticsParser.Parse(output);
        var serialized = System.Text.Json.JsonSerializer.Serialize(report);

        Assert.Equal(AndroidNetworkRegistrationState.InService, report.VoiceRegistration);
        Assert.Equal(AndroidNetworkRegistrationState.OutOfService, report.DataRegistration);
        Assert.Equal("LTE", report.VoiceRadioTechnology);
        Assert.Equal("EHRPD", report.DataRadioTechnology);
        Assert.DoesNotContain("123456789012345", serialized);
        Assert.DoesNotContain("001010123456789", serialized);
        Assert.DoesNotContain("89012345678901234567", serialized);
    }

    [Fact]
    public void BoundsAndSanitizesOperatorLabel()
    {
        var report = AndroidTelephonyDiagnosticsParser.Parse(
            "mOperatorAlphaLong=Example\r\nNetwork, mVoiceRegState=0(IN_SERVICE)");

        Assert.DoesNotContain('\r', report.OperatorName);
        Assert.DoesNotContain('\n', report.OperatorName);
        Assert.True(report.OperatorName.Length <= 80);
        Assert.Equal("Example", report.OperatorName);
    }
}
