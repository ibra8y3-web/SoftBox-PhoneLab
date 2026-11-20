using SoftBox.PhoneLab.Core.Fastboot;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class FastbootVariableParserTests
{
    [Fact]
    public void ParsesCommonBootloaderIdentityLockSecureBootAndSlots()
    {
        const string output = """
            (bootloader) product: oriole
            (bootloader) variant: raven
            (bootloader) hw-revision: EVT1.2
            (bootloader) version-bootloader: cloudripper-1.2
            (bootloader) version-baseband: g5123b-12345
            (bootloader) unlocked: no
            (bootloader) secure: yes
            (bootloader) current-slot: a
            (bootloader) slot-count: 2
            (bootloader) finished. total time: 0.006s
            """;

        var result = FastbootVariableParser.Parse(output);

        Assert.Equal("oriole", result.Product);
        Assert.Equal("raven", result.Variant);
        Assert.Equal("EVT1.2", result.HardwareRevision);
        Assert.Equal("cloudripper-1.2", result.BootloaderVersion);
        Assert.Equal("g5123b-12345", result.BasebandVersion);
        Assert.Equal(BootloaderLockState.Locked, result.LockState);
        Assert.Equal(VerifiedBootState.Enabled, result.SecureBootState);
        Assert.Equal("a", result.CurrentSlot);
        Assert.Equal(2, result.SlotCount);
        Assert.Empty(result.ConflictingVariables);
    }

    [Fact]
    public void ParsesRollbackVariablesAsRawEvidenceWithoutInterpretingPolicy()
    {
        const string output = """
            (bootloader) anti: 4
            (bootloader) antirollback-index: 0x00000005
            (bootloader) rollback-index-boot: 12
            """;

        var result = FastbootVariableParser.Parse(output);

        Assert.Equal("4", result.RollbackVariables["anti"]);
        Assert.Equal("0x00000005", result.RollbackVariables["antirollback-index"]);
        Assert.Equal("12", result.RollbackVariables["rollback-index-boot"]);
        Assert.Contains(result.Warnings, warning => warning.Contains("كما أبلغ بها الجهاز فقط", StringComparison.Ordinal));
    }

    [Fact]
    public void ContradictoryLockEvidenceIsConflictingRatherThanChoosingOneValue()
    {
        const string output = """
            (bootloader) unlocked: yes
            (bootloader) device-state: locked
            """;

        var result = FastbootVariableParser.Parse(output);

        Assert.Equal(BootloaderLockState.Conflicting, result.LockState);
        Assert.Contains(result.Warnings, warning => warning.Contains("متعارضة", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateConflictingVariableValuesAreRemovedFromResolvedValues()
    {
        const string output = """
            (bootloader) product: oriole
            (bootloader) product: panther
            (bootloader) unlocked: maybe
            """;

        var result = FastbootVariableParser.Parse(output);

        Assert.Null(result.Product);
        Assert.Contains("product", result.ConflictingVariables);
        Assert.Equal(BootloaderLockState.Unknown, result.LockState);
    }

    [Fact]
    public void FailedAndUnknownGetvarResponsesDoNotBecomeDeviceFacts()
    {
        const string output = """
            getvar:product FAILED (remote: 'GetVar Variable Not found')
            getvar:anti FAILED (remote: unknown variable)
            (bootloader) finished. total time: 0.002s
            """;

        var result = FastbootVariableParser.Parse(output);

        Assert.Empty(result.Variables);
        Assert.Null(result.Product);
        Assert.Empty(result.RollbackVariables);
        Assert.Equal(BootloaderLockState.Unknown, result.LockState);
        Assert.Equal(VerifiedBootState.Unknown, result.SecureBootState);
    }

    [Fact]
    public void SecureBootEvidenceDoesNotImplyBootloaderLockState()
    {
        var result = FastbootVariableParser.Parse("(bootloader) secure: yes");

        Assert.Equal(VerifiedBootState.Enabled, result.SecureBootState);
        Assert.Equal(BootloaderLockState.Unknown, result.LockState);
    }

    [Theory]
    [InlineData("a", "a", 2)]
    [InlineData("b", "b", 2)]
    [InlineData("c", "c", 4)]
    [InlineData("unknown", null, 0)]
    public void SlotValuesAreNormalizedAndCountsAreRangeChecked(
        string slot,
        string? expectedSlot,
        int count)
    {
        var result = FastbootVariableParser.Parse(
            $"(bootloader) current-slot: {slot}\n(bootloader) slot-count: {count}");

        Assert.Equal(expectedSlot, result.CurrentSlot);
        Assert.Equal(count is >= 1 and <= 8 ? count : null, result.SlotCount);
    }

    [Fact]
    public void EmptyOrUnrelatedOutputReturnsUnknownStateWithoutInventingVariables()
    {
        var result = FastbootVariableParser.Parse(
            "< waiting for any device >\nFastboot version: 35.0.2\nFinished. Total time: 0.01s");

        Assert.Empty(result.Variables);
        Assert.Equal(BootloaderLockState.Unknown, result.LockState);
        Assert.Equal(VerifiedBootState.Unknown, result.SecureBootState);
        Assert.Null(result.Product);
    }
}
