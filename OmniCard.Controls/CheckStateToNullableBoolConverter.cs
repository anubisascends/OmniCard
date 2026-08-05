using System.Globalization;
using System.Windows.Data;

namespace OmniCard.Controls;

public sealed class CheckStateToNullableBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TagCheckState.Checked => true,
        TagCheckState.Indeterminate => null,
        _ => false,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
