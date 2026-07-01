using System.Globalization;
using System.Windows.Data;

namespace OmniCard.Helpers;

/// <summary>
/// Converts an enum value to bool by comparing it to the ConverterParameter string.
/// Used for binding ToggleButton.IsChecked to a single enum property.
/// </summary>
public class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
