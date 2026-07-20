using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using OmniCard.Models;

namespace OmniCard.Controls.Converters;

/// <summary>Maps a card's active <see cref="ListingStatus"/> to the tile badge text
/// ("LISTED"/"PICKED"); returns "" (and the badge is hidden) when the card is not
/// on-market. Pair with <see cref="NullToCollapsedConverter"/> for the badge's Visibility.</summary>
public class ListingStatusToBadgeConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ListingStatus s ? (s == ListingStatus.Picked ? "PICKED" : "LISTED") : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
