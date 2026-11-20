using SoftBox.PhoneLab.Core.Models;
using Xunit;

namespace SoftBox.PhoneLab.Tests;

public sealed class WorkshopInternationalRulesTests
{
    [Theory]
    [InlineData(" egp ", "EGP")]
    [InlineData("usd", "USD")]
    [InlineData("eur", "EUR")]
    [InlineData("gbp", "GBP")]
    public void CurrencyCodesNormalizeToUppercaseIsoShape(string input, string expected)
        => Assert.Equal(expected, WorkshopInternationalRules.NormalizeCurrencyCode(input));

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("12A")]
    public void InvalidCurrencyCodesAreRejected(string input)
        => Assert.Throws<ArgumentException>(() => WorkshopInternationalRules.NormalizeCurrencyCode(input));

    [Fact]
    public void MoneyFormattingUsesSelectedLocaleAndExplicitCurrencyCode()
    {
        var actual = WorkshopInternationalRules.FormatMoney(1234.5m, "USD", "en-US");
        Assert.Equal("1,234.50 USD", actual);
    }

    [Fact]
    public void WarrantyEndIsCalculatedFromDeliveryAndCalendarMonths()
    {
        var delivered = new DateTimeOffset(2026, 1, 31, 10, 0, 0, TimeSpan.Zero);
        var end = WorkshopInternationalRules.CalculateWarrantyEnd(delivered, 1);
        Assert.Equal(new DateTimeOffset(2026, 2, 28, 10, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void NoWarrantyOrUndeliveredOrderHasNoExpiry()
    {
        Assert.Null(WorkshopInternationalRules.CalculateWarrantyEnd(null, 3));
        Assert.Null(WorkshopInternationalRules.CalculateWarrantyEnd(DateTimeOffset.UtcNow, 0));
    }

    [Fact]
    public void WarrantyExpiryRejectsUnreasonableDurations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkshopInternationalRules.CalculateWarrantyEnd(DateTimeOffset.UtcNow, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkshopInternationalRules.CalculateWarrantyEnd(DateTimeOffset.UtcNow, 121));
    }

    [Fact]
    public void WarrantyIsActiveThroughTheExpiryInstant()
    {
        var expiry = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.True(WorkshopInternationalRules.IsWarrantyActive(expiry, expiry));
        Assert.False(WorkshopInternationalRules.IsWarrantyActive(expiry, expiry.AddTicks(1)));
        Assert.False(WorkshopInternationalRules.IsWarrantyActive(null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CultureNamesAreValidatedAndCanonicalized()
        => Assert.Equal("en-US", WorkshopInternationalRules.NormalizeCultureName("en-us"));
}
