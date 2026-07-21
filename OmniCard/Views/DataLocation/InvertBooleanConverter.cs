using System.Globalization;
using System.Windows.Data;

namespace OmniCard.Views.DataLocation;

/// <summary>Inverts a bool (used by the Data Location settings section to disable controls
/// while a migration is running).</summary>
public class InvertBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
