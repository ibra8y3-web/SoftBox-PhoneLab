using SoftBox.PhoneLab.Core.Workflow;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class ReadOnlyWorkflowPolicyTests
{
    [Fact]
    public void ValidateAcceptsKnownReadOnlyWorkflowSteps()
    {
        var workflow = new ReadOnlyWorkflowDefinition(1, "فحص قبل الإصلاح",
        [
            new("properties"),
            new("battery"),
            new("storage")
        ]);

        var result = ReadOnlyWorkflowPolicy.Validate(workflow);

        Assert.Equal("فحص قبل الإصلاح", result.Name);
        Assert.Equal(3, result.Steps.Count);
        Assert.Equal("قراءة مساحة التخزين", ReadOnlyWorkflowPolicy.GetStepLabel("storage"));
    }

    [Theory]
    [InlineData("shell")]
    [InlineData("custom-command")]
    [InlineData("delete-files")]
    [InlineData("flash-firmware")]
    public void ValidateRejectsUnknownOrUnsafeSteps(string stepId)
    {
        var workflow = new ReadOnlyWorkflowDefinition(1, "Unsafe", [new(stepId)]);
        Assert.Throws<InvalidDataException>(() => ReadOnlyWorkflowPolicy.Validate(workflow));
    }

    [Fact]
    public void ValidateRejectsEmptyOrOversizedWorkflow()
    {
        Assert.Throws<InvalidDataException>(() =>
            ReadOnlyWorkflowPolicy.Validate(new ReadOnlyWorkflowDefinition(1, "Empty", [])));

        var tooMany = Enumerable.Range(0, ReadOnlyWorkflowPolicy.MaximumSteps + 1)
            .Select(_ => new ReadOnlyWorkflowStep("battery")).ToArray();
        Assert.Throws<InvalidDataException>(() =>
            ReadOnlyWorkflowPolicy.Validate(new ReadOnlyWorkflowDefinition(1, "Long", tooMany)));
    }

    [Fact]
    public void ValidateRejectsUnsupportedSchemaAndInvalidName()
    {
        Assert.Throws<InvalidDataException>(() =>
            ReadOnlyWorkflowPolicy.Validate(new ReadOnlyWorkflowDefinition(2, "Version", [new("battery")])));
        Assert.Throws<InvalidDataException>(() =>
            ReadOnlyWorkflowPolicy.Validate(new ReadOnlyWorkflowDefinition(1, " ", [new("battery")])));
    }
}
