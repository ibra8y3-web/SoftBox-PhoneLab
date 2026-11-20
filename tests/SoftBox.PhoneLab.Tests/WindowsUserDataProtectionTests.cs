using System.Security.Cryptography;
using System.Text;
using SoftBox.PhoneLab.Core.Security;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class WindowsUserDataProtectionTests
{
    [Fact]
    public void ProtectsAndRestoresDataForTheSameWindowsUserAndPurpose()
    {
        var plaintext = Encoding.UTF8.GetBytes("""{"CustomerName":"Private Customer","Phone":"01000000000"}""");
        var protectedBytes = WindowsUserDataProtection.Protect(plaintext, "WorkshopStore.v1");

        Assert.True(WindowsUserDataProtection.IsProtected(protectedBytes));
        Assert.False(ContainsSequence(protectedBytes, Encoding.UTF8.GetBytes("Private Customer")));
        Assert.False(ContainsSequence(protectedBytes, Encoding.UTF8.GetBytes("01000000000")));

        var restored = WindowsUserDataProtection.UnprotectOrLegacy(
            protectedBytes, "WorkshopStore.v1", out var wasProtected);

        Assert.True(wasProtected);
        Assert.Equal(plaintext, restored);
    }

    [Fact]
    public void PurposeSeparationPreventsUsingAnActivityFileAsAWorkshopDatabase()
    {
        var protectedBytes = WindowsUserDataProtection.Protect(
            Encoding.UTF8.GetBytes("""{"Secret":"Workshop"}"""), "WorkshopStore.v1");

        Assert.Throws<CryptographicException>(() =>
            WindowsUserDataProtection.UnprotectOrLegacy(
                protectedBytes, "LocalActivityStore.v1", out _));
    }

    [Fact]
    public void LegacyPlaintextIsReturnedForExplicitMigration()
    {
        var legacy = Encoding.UTF8.GetBytes("""{"SchemaVersion":1}""");

        var restored = WindowsUserDataProtection.UnprotectOrLegacy(
            legacy, "WorkshopStore.v1", out var wasProtected);

        Assert.False(wasProtected);
        Assert.Equal(legacy, restored);
    }

    private static bool ContainsSequence(byte[] source, byte[] value)
        => source.AsSpan().IndexOf(value) >= 0;
}
