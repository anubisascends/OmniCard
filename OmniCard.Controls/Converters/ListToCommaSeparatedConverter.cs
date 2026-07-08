using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace OmniCard.Controls.Converters;

public class ListToCommaSeparatedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is IList<string> list ? string.Join(", ", list) : "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str || string.IsNullOrWhiteSpace(str))
            return new List<string>();

        return str.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
