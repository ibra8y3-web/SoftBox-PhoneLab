using SoftBox.PhoneLab.Core.Diagnostics;
using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class MeasurementReferenceProfileTests
{
    [Fact]
    public void EvaluatesActualFixtureSampleAgainstTechnicianSuppliedLimits()
    {
        var profile = new MeasurementReferenceProfile(1, "Bench A", "SM-A546E",
            "OEM board service manual page 14",
        [
            new("usbVoltageV", "V", 4.75, 5.25),
            new("rail:VDD_MAIN", "V", 3.8, 4.2),
            new("boardTemperatureC", "C", 0, 50)
        ]);
        var sample = new ExternalMeasurementSample("Fixture", DateTimeOffset.UtcNow,
            5.0, 3.9, .2, 33, 1.0, new Dictionary<string, double> { ["VDD_MAIN"] = 4.0 });

        var results = MeasurementReferenceProfilePolicy.Evaluate(profile, sample);

        Assert.Equal(MeasurementLimitState.Passed, results[0].State);
        Assert.Equal(MeasurementLimitState.Passed, results[1].State);
        Assert.Equal(MeasurementLimitState.Passed, results[2].State);
    }

    [Fact]
    public void OutOfRangeAndNotMeasuredValuesRemainDistinct()
    {
        var profile = new MeasurementReferenceProfile(1, "Bench", "Any",
            "Technician-entered reference",
        [
            new("usbVoltageV", "V", 4.75, 5.25),
            new("rail:VDD_CPU", "V", .7, .9)
        ]);
        var sample = new ExternalMeasurementSample("Fixture", DateTimeOffset.UtcNow,
            3.3, null, null, null, null, new Dictionary<string, double>());

        var results = MeasurementReferenceProfilePolicy.Evaluate(profile, sample);
        Assert.Equal(MeasurementLimitState.OutOfRange, results[0].State);
        Assert.Equal(MeasurementLimitState.NotMeasured, results[1].State);
    }

    [Fact]
    public void RejectsInventedOrInvalidMeasurementBounds()
    {
        var invalid = new MeasurementReferenceProfile(1, "Bad", "Any", "Test",
            [new("usbVoltageV", "V", 5.5, 4.5)]);
        Assert.Throws<InvalidDataException>(() => MeasurementReferenceProfilePolicy.Validate(invalid));
        Assert.Throws<InvalidDataException>(() => MeasurementReferenceProfilePolicy.Validate(
            invalid with { Limits = [new("unlockBootloader", "", 0, 1)] }));
    }
}
