using System.Globalization;
using OmniCard.Controls.Converters;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class ListingBadgeConverterTests
{
    private static object Run(ListingStatus? listing, EbayListingStatus? ebay)
        => new ListingBadgeConverter().Convert(
            [listing, ebay], typeof(string), null, CultureInfo.InvariantCulture);

    [Fact]
    public void ActiveEbay_TakesPrecedence_ReturnsEbay()
        => Assert.Equal("eBAY", Run(ListingStatus.Listed, EbayListingStatus.Active));

    [Fact]
    public void ActiveEbay_WithNoGenericStatus_ReturnsEbay()
        => Assert.Equal("eBAY", Run(null, EbayListingStatus.Active));

    [Fact]
    public void EndedEbay_FallsBackToGeneric()
        => Assert.Equal("LISTED", Run(ListingStatus.Listed, EbayListingStatus.Ended));

    [Fact]
    public void Picked_NoEbay_ReturnsPicked()
        => Assert.Equal("PICKED", Run(ListingStatus.Picked, null));

    [Fact]
    public void Listed_NoEbay_ReturnsListed()
        => Assert.Equal("LISTED", Run(ListingStatus.Listed, null));

    [Fact]
    public void Nothing_ReturnsEmpty()
        => Assert.Equal("", Run(null, null));
}
