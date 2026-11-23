using SoftBox.PhoneLab.Core.Diagnostics;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class IPhoneAssessmentInputValidatorTests
{
    [Theory]
    [InlineData("line one\nline two", "line one line two")]
    [InlineData("line one\r\nline two", "line one line two")]
    [InlineData("left\tright", "left right")]
    [InlineData("  trimmed note  ", "trimmed note")]
    public void NormalizesMultilineNotesForDiagnosticEvidenceStorage(string input, string expected)
        => Assert.Equal(expected, IPhoneAssessmentInputValidator.NormalizeNote(input));

    [Fact]
    public void RejectsRemainingControlCharacters()
        => Assert.Throws<ArgumentException>(() =>
            IPhoneAssessmentInputValidator.NormalizeNote("bad\u0001note"));

    [Fact]
    public void RejectsNotesLongerThanTheConfiguredBound()
        => Assert.Throws<ArgumentException>(() =>
            IPhoneAssessmentInputValidator.NormalizeNote(new string('x', 1001)));

    [Fact]
    public void RejectsInvalidMaximumLength()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            IPhoneAssessmentInputValidator.NormalizeNote("note", 0));
}
