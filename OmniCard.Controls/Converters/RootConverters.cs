using System.Collections;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using OmniCard.Imaging;
using OmniCard.Models;

namespace OmniCard.Controls.Converters;

/// <summary>
/// Attached property that stores a string tag on any DependencyObject,
/// including DataGridColumn which is not a FrameworkElement.
/// </summary>
public static class ColumnTag
{
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached(
            "Key",
            typeof(string),
            typeof(ColumnTag),
            new PropertyMetadata(null));

    public static string? GetKey(DependencyObject obj) => (string?)obj.GetValue(KeyProperty);
    public static void SetKey(DependencyObject obj, string? value) => obj.SetValue(KeyProperty, value);
}

public class BoolToVisibilityConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class InverseBoolToVisibilityConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class InverseBoolConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class BoolToEbayStatusConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "eBay: Connected" : "eBay: Not Connected";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class BoolToConnectionColorConverter : MarkupExtension, IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush Green = new(System.Windows.Media.Colors.Green);
    private static readonly System.Windows.Media.SolidColorBrush Gray = new(System.Windows.Media.Colors.Gray);

    static BoolToConnectionColorConverter()
    {
        Green.Freeze();
        Gray.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Green : Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class NullToCollapsedConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class NullToVisibilityConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class NullToBoolConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class NullToVisibleConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class ListToStringConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable list)
            return string.Join(", ", list.Cast<object>());
        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class CardGameDisplayConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            CardGame.Mtg => "Magic: The Gathering",
            CardGame.OnePiece => "One Piece TCG",
            _ => value?.ToString() ?? ""
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class ScanQualityDisplayConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            ScanQuality.Fast => "Fast (200 DPI)",
            ScanQuality.HighQuality => "High Quality",
            _ => value?.ToString() ?? ""
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class LocationDisplayConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CollectionCard card || card.Container is null)
            return "";

        var container = card.Container;
        return container.ContainerType switch
        {
            ContainerType.Binder when card.Page.HasValue && card.Slot.HasValue =>
                $"{container.Name} (P{card.Page}/S{card.Slot})",
            ContainerType.Binder when card.Page.HasValue =>
                $"{container.Name} (P{card.Page})",
            ContainerType.Box when !string.IsNullOrEmpty(card.Section) =>
                $"{container.Name} - {card.Section}",
            _ => container.Name,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class SetInfoDisplayConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SetInfo s && s.SetCode.Length > 0 ? $"{s.SetName} ({s.SetCode})" : "All Sets";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class CompletionPercentConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SetCompletionSummary s ? $"{s.OwnedCount}/{s.TotalCount} ({s.CompletionPercent:F0}%)" : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class ConfidenceToColorConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double confidence)
            return System.Windows.Media.Brushes.Gray;

        return confidence switch
        {
            >= 80 => System.Windows.Media.Brushes.LimeGreen,
            >= 50 => System.Windows.Media.Brushes.Orange,
            _ => System.Windows.Media.Brushes.OrangeRed
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class LowConfidenceToVisibleConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double confidence and < 80 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class CardPreviewImageConverter : MarkupExtension, IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CollectionCard card) return null;

        // Try scan image from cache first
        if (card.ScanImagePath is not null && ScanImageCache.Instance is not null)
        {
            var dataDir = parameter as string ?? "";
            var fullPath = System.IO.Path.Combine(dataDir, card.ScanImagePath);
            var cached = ScanImageCache.Instance.GetImage(fullPath);
            if (cached is not null)
                return cached;
        }

        // Fall back to API image via cache
        if (card.ImageUri is not null)
            return CardArtCache.Instance?.GetImage(null, card.ImageUri);

        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class PriceDeltaColorConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal delta) return System.Windows.Media.Brushes.Gray;
        return delta >= 0 ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.OrangeRed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class ContainerTypeDisplayConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ContainerType ct ? ct switch
        {
            ContainerType.Binder => "Binders",
            ContainerType.Box => "Boxes",
            ContainerType.DeckBox => "Deck Boxes",
            ContainerType.DisplayCase => "Display Cases",
            _ => value.ToString() ?? ""
        } : "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

public class FoilToFinishConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Foil" : "Normal";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>
/// Compares an enum value to the ConverterParameter string.
/// Returns true when they match; sets the enum value on ConvertBack.
/// Used to bind RadioButton.IsChecked to an enum property.
/// </summary>
public class EnumBoolConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null) return Binding.DoNothing;
        return Enum.Parse(targetType, parameter.ToString()!);
    }

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>
/// Resolves the tile image for a collection card. Bindings, in order:
/// [0] CollectionCard, [1] bool IsStacked, [2] string data directory.
/// Returns the first available ImageSource (see <see cref="CardArtCandidateResolver"/>),
/// or null when no art is available (the tile then shows a placeholder).
/// </summary>
public class TileArtConverter : MarkupExtension, IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not CollectionCard card)
            return null;

        var isStacked = values[1] is true;
        var dataDir = values[2] as string ?? "";

        foreach (var candidate in CardArtCandidateResolver.Resolve(card, isStacked))
        {
            ImageSource? image = candidate.Kind switch
            {
                CardArtKind.Scan =>
                    ScanImageCache.Instance?.GetImage(Path.Combine(dataDir, candidate.Value)),
                CardArtKind.Downloaded =>
                    CardArtCache.Instance?.GetImage(null, candidate.Value),
                _ => null
            };

            if (image is not null)
                return image;
        }

        return null;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
