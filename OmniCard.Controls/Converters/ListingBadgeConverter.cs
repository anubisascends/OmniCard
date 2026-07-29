using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using OmniCard.Models;

namespace OmniCard.Controls.Converters;

/// <summary>
/// Tile badge text combining the general on-market status with the eBay listing status.
/// An active eBay listing takes precedence and shows "eBAY"; otherwise falls back to the
/// generic "PICKED"/"LISTED" from <see cref="ListingStatus"/>; "" (hidden) when neither.
/// Bind as a MultiBinding over [ListingStatus, EbayListing.Status].
/// </summary>
public class ListingBadgeConverter : MarkupExtension, IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var listingStatus = values.Length > 0 ? values[0] as ListingStatus? : null;
        var ebayStatus = values.Length > 1 ? values[1] as EbayListingStatus? : null;

        if (ebayStatus == EbayListingStatus.Active)
            return "eBAY";

        return listingStatus switch
        {
            ListingStatus.Picked => "PICKED",
            ListingStatus.Listed => "LISTED",
            _ => "",
        };
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
