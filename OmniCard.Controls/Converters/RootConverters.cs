using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using OmniCard.Models;

namespace OmniCard.Controls.Converters;

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

/// <summary>Visible when the bound string is non-null and non-empty (whitespace-only counts as
/// empty); used to show/hide inline status/error text such as a refresh failure message.</summary>
public class StringToVisibilityConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;

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
            CardGame.Riftbound => "Riftbound",
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

public class MarketPriceDisplayConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d && d > 0 ? $"${d:F2}" : "";

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
/// Dashboard breakdown row keys are raw enum names ("Mtg", "OnePiece", "Single", "Box", ...)
/// or, for the by-location breakdown, a storage container name/"Unassigned". Maps the known
/// enum names to friendly display text and passes everything else through unchanged.
/// </summary>
public class BreakdownKeyDisplayConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrEmpty(key)) return "";

        if (Enum.TryParse<CardGame>(key, out var game))
        {
            return game switch
            {
                CardGame.Mtg => "Magic: The Gathering",
                CardGame.OnePiece => "One Piece TCG",
                CardGame.Riftbound => "Riftbound",
                _ => key
            };
        }

        if (Enum.TryParse<ProductCategory>(key, out var category))
        {
            return category switch
            {
                ProductCategory.Single => "Singles",
                ProductCategory.Case => "Case",
                ProductCategory.Box => "Box",
                ProductCategory.Pack => "Pack",
                ProductCategory.Deck => "Deck",
                ProductCategory.Bundle => "Bundle",
                ProductCategory.Other => "Other",
                _ => key
            };
        }

        return key; // storage location name, "Unassigned", or "Unknown" — shown as-is
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>Formats a decimal as a signed dollar amount, e.g. "+$12.34" / "-$12.34".</summary>
public class SignedMoneyConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d
            ? d >= 0 ? $"+${d:N2}" : $"-${Math.Abs(d):N2}"
            : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>Green when the decimal value is non-negative, red otherwise. Used for gain/loss tiles.</summary>
public class GainLossColorConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d
            ? d >= 0 ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.OrangeRed
            : System.Windows.Media.Brushes.Gray;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>
/// Multi-value converter for a breakdown row's Unrealized/Profit column: values[0] minus
/// values[1] (e.g. Market-Cost, or Proceeds-Cost), formatted as a signed dollar amount.
/// </summary>
public class DeltaMoneyConverter : MarkupExtension, IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var a = values.Length > 0 && values[0] is decimal da ? da : 0m;
        var b = values.Length > 1 && values[1] is decimal db ? db : 0m;
        var delta = a - b;
        return delta >= 0 ? $"+${delta:N2}" : $"-${Math.Abs(delta):N2}";
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>
/// Assigns a categorical bar-chart color by a fixed slot order (never by rank/value), keyed off
/// an <c>ItemsControl.AlternationIndex</c> (0-based, cycling if a chart has more rows than
/// slots). Hues are the dataviz-skill categorical palette's dark-mode steps — validated for
/// CVD-safe adjacent contrast on a dark chart surface, matching this app's fixed dark
/// MaterialDesign theme (see App.xaml, BaseTheme="Dark").
/// </summary>
public class SeriesIndexBrushConverter : MarkupExtension, IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush[] Palette = CreatePalette();

    private static System.Windows.Media.SolidColorBrush[] CreatePalette()
    {
        string[] hex =
        [
            "#3987E5", // 1 blue
            "#199E70", // 2 aqua
            "#C98500", // 3 yellow
            "#008300", // 4 green
            "#9085E9", // 5 violet
            "#E66767", // 6 red
            "#D55181", // 7 magenta
            "#D95926", // 8 orange
        ];
        var brushes = new System.Windows.Media.SolidColorBrush[hex.Length];
        for (var i = 0; i < hex.Length; i++)
        {
            var color = (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(hex[i]);
            var brush = new System.Windows.Media.SolidColorBrush(color);
            brush.Freeze();
            brushes[i] = brush;
        }
        return brushes;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var index = value is int i ? i : 0;
        if (index < 0) index = 0;
        return Palette[index % Palette.Length];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>
/// Multi-value converter for an inline breakdown proportion bar: values[0] is the row's amount,
/// values[1] is the grand total. Returns a bar width in device-independent pixels, scaled against
/// a max width (ConverterParameter, default 80).
/// </summary>
public class ShareBarWidthConverter : MarkupExtension, IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var amount = values.Length > 0 && values[0] is decimal da ? da : 0m;
        var total = values.Length > 1 && values[1] is decimal dt ? dt : 0m;
        var maxWidth = parameter is string s && double.TryParse(s, culture, out var mw) ? mw : 80.0;

        if (total <= 0m || amount <= 0m) return 0.0;

        var ratio = (double)(amount / total);
        if (ratio > 1.0) ratio = 1.0;
        if (ratio < 0.0) ratio = 0.0;
        return ratio * maxWidth;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>Maps a <see cref="SalesChannel"/> to its display label for order cards/badges.</summary>
public class SalesChannelDisplayConverter : MarkupExtension, IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        SalesChannel.TcgPlayer => "TCGplayer",
        SalesChannel.Ebay => "eBay",
        SalesChannel.Manual => "Manual",
        _ => value?.ToString() ?? "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}

/// <summary>Maps an <see cref="OrderStatus"/> to the kanban accent colour used on card stripes.
/// Kept in sync with the per-column header accents in OrdersView.xaml.</summary>
public class OrderStatusToAccentBrushConverter : MarkupExtension, IValueConverter
{
    private static readonly SolidColorBrush Created = Frozen(0x21, 0x96, 0xF3);   // blue
    private static readonly SolidColorBrush Packed = Frozen(0xFF, 0x98, 0x00);    // amber
    private static readonly SolidColorBrush Shipped = Frozen(0x00, 0x96, 0x88);   // teal
    private static readonly SolidColorBrush Completed = Frozen(0x4C, 0xAF, 0x50); // green
    private static readonly SolidColorBrush Cancelled = Frozen(0x9E, 0x9E, 0x9E); // grey

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        OrderStatus.Created => Created,
        OrderStatus.Packed => Packed,
        OrderStatus.Shipped => Shipped,
        OrderStatus.Completed => Completed,
        _ => Cancelled,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
