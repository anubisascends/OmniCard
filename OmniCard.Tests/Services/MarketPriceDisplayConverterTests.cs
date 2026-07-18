using System.Globalization;
using OmniCard.Controls.Converters;
using Xunit;

namespace OmniCard.Tests.Services;

public class MarketPriceDisplayConverterTests
{
    private static object Run(decimal value)
        => new MarketPriceDisplayConverter().Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

    [Fact]
    public void PositivePrice_FormattedWithDollarAndTwoDecimals()
        => Assert.Equal("$1.20", Run(1.2m));

    [Fact]
    public void Zero_ReturnsEmpty()
        => Assert.Equal("", Run(0m));

    [Fact]
    public void Negative_ReturnsEmpty()
        => Assert.Equal("", Run(-3m));
}
