using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OmniCard.Models;

namespace OmniCard.Controls.Converters;

/// <summary>Resolves a <see cref="CardGame"/> to its bundled card-back image
/// (<c>Resources/CardBacks/{slug}.png</c>) for the binder's reverse-side pocket hint. Returns
/// <c>null</c> when no image is bundled for that game (the user supplies these PNGs later), so the
/// slot's generic vector back shows through instead of a broken image. Results — including the null
/// "no asset" answer — are cached per slug so a missing file isn't re-probed on every bind.</summary>
public class CardBackImageConverter : MarkupExtension, IValueConverter
{
    private static readonly Dictionary<string, ImageSource?> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CardGame game) return null;

        var slug = CardBackAssets.Slug(game);
        if (Cache.TryGetValue(slug, out var cached)) return cached;

        ImageSource? image = null;
        try
        {
            var uri = new Uri($"pack://application:,,,/OmniCard;component/Resources/CardBacks/{slug}.png");
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = uri;
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
        }
        catch
        {
            // No bundled back for this game — leave null so the generic vector back shows.
        }

        Cache[slug] = image;
        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;
}
