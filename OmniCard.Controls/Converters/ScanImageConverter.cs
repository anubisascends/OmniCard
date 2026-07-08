using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using OmniCard.Imaging;

namespace OmniCard.Controls.Converters;

public class ScanImageConverter : MarkupExtension, IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || ScanImageCache.Instance is null)
            return null;

        return ScanImageCache.Instance.GetImage(path);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
