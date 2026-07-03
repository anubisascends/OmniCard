// OmniCard/Views/SetFilterBuilder/SymbolToDrawingBrushConverter.cs
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OmniCard.Views.SetFilterBuilder;

public class SymbolToDrawingBrushConverter : IValueConverter
{
    public static readonly SymbolToDrawingBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DrawingImage drawingImage)
            return null;

        var brush = new DrawingBrush(drawingImage.Drawing) { Stretch = Stretch.Uniform };
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
