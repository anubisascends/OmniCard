using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using OmniCard.Services;

namespace OmniCard.Views.Root;

public class MatchedArtConverter : MarkupExtension, IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (CardArtCache.Instance is null)
            return null;

        var localPath = values.Length > 0 ? values[0] as string : null;
        var imageUri = values.Length > 1 ? values[1] as string : null;

        return CardArtCache.Instance.GetImage(localPath, imageUri);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
