using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OmniCard.Helpers;

public static class SetSymbol
{
    private static SetSymbolCache? _cache;

    public static void Initialize(SetSymbolCache cache) => _cache = cache;

    // Attached property: SetCode
    public static readonly DependencyProperty SetCodeProperty =
        DependencyProperty.RegisterAttached("SetCode", typeof(string), typeof(SetSymbol),
            new PropertyMetadata(null, OnPropertyChanged));

    public static string? GetSetCode(DependencyObject obj) => (string?)obj.GetValue(SetCodeProperty);
    public static void SetSetCode(DependencyObject obj, string? value) => obj.SetValue(SetCodeProperty, value);

    // Attached property: Rarity
    public static readonly DependencyProperty RarityProperty =
        DependencyProperty.RegisterAttached("Rarity", typeof(string), typeof(SetSymbol),
            new PropertyMetadata(null, OnPropertyChanged));

    public static string? GetRarity(DependencyObject obj) => (string?)obj.GetValue(RarityProperty);
    public static void SetRarity(DependencyObject obj, string? value) => obj.SetValue(RarityProperty, value);

    private static readonly Dictionary<string, SolidColorBrush> RarityBrushes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["common"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1A1A")),
        ["uncommon"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A0A0")),
        ["rare"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CFB53B")),
        ["mythic"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E85D26")),
    };

    static SetSymbol()
    {
        foreach (var brush in RarityBrushes.Values)
            brush.Freeze();
    }

    private static async void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border || _cache == null)
            return;

        var setCode = GetSetCode(border);
        var rarity = GetRarity(border);

        if (string.IsNullOrEmpty(setCode) || string.IsNullOrEmpty(rarity))
        {
            border.Background = null;
            border.OpacityMask = null;
            return;
        }

        try
        {
            var drawing = await _cache.GetSetSymbolAsync(setCode, rarity);
            if (drawing != null)
            {
                // Use the underlying Drawing directly as a DrawingBrush for the opacity mask.
                // DrawingBrush preserves vector fidelity at any size, unlike ImageBrush which
                // rasterizes at a fixed resolution and becomes blurry when scaled up.
                var drawingBrush = new DrawingBrush(drawing.Drawing) { Stretch = Stretch.Uniform };
                drawingBrush.Freeze();
                border.OpacityMask = drawingBrush;

                if (RarityBrushes.TryGetValue(rarity, out var brush))
                    border.Background = brush;
                else
                    border.Background = RarityBrushes["common"];

                // Set informative tooltip
                var setName = _cache.GetSetName(setCode);
                var rarityDisplay = SetSymbolCache.FormatRarityDisplay(rarity);
                border.ToolTip = setName is not null
                    ? $"{setName} ({setCode.ToUpperInvariant()})\n{rarityDisplay}"
                    : $"{setCode.ToUpperInvariant()}\n{rarityDisplay}";
            }
            else
            {
                border.Background = null;
                border.OpacityMask = null;
            }
        }
        catch
        {
            border.Background = null;
            border.OpacityMask = null;
        }
    }
}
