using System.Globalization;
using OmniCard.Controls.Converters;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ListingStatusToBadgeConverterTests
{
    private static object Run(ListingStatus? value)
        => new ListingStatusToBadgeConverter().Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

    [Fact]
    public void Listed_ReturnsListedText()
        => Assert.Equal("LISTED", Run(ListingStatus.Listed));

    [Fact]
    public void Picked_ReturnsPickedText()
        => Assert.Equal("PICKED", Run(ListingStatus.Picked));

    [Fact]
    public void Null_ReturnsEmpty()
        => Assert.Equal("", Run(null));
}
