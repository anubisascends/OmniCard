using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OmniCard.Imaging;
using OmniCard.Models;

namespace OmniCard.Controls;

/// <summary>
/// Attached behavior that fills an <see cref="Image"/> with a collection card's art
/// without blocking the UI thread. Downloaded (API) art is preferred and loads asynchronously,
/// leaving <see cref="Image.Source"/> null (so the tile placeholder shows) until the download
/// completes; the scanned image is the fallback and loads synchronously (a local file).
/// Art source order comes from <see cref="CardArtCandidateResolver"/> (downloaded then scan).
/// </summary>
public static class TileArt
{
    public static readonly DependencyProperty CardProperty =
        DependencyProperty.RegisterAttached(
            "Card", typeof(CollectionCard), typeof(TileArt),
            new PropertyMetadata(null, OnChanged));

    public static CollectionCard? GetCard(DependencyObject o) => (CollectionCard?)o.GetValue(CardProperty);
    public static void SetCard(DependencyObject o, CollectionCard? v) => o.SetValue(CardProperty, v);

    public static readonly DependencyProperty DataDirectoryProperty =
        DependencyProperty.RegisterAttached(
            "DataDirectory", typeof(string), typeof(TileArt),
            new PropertyMetadata("", OnChanged));

    public static string GetDataDirectory(DependencyObject o) => (string)o.GetValue(DataDirectoryProperty);
    public static void SetDataDirectory(DependencyObject o, string v) => o.SetValue(DataDirectoryProperty, v);

    // Generation token: only the newest scheduled update runs, so the initial
    // property sets (Card/DataDirectory) coalesce into one resolve, and a stale
    // async download that finishes after the inputs changed is ignored.
    private static readonly DependencyProperty TokenProperty =
        DependencyProperty.RegisterAttached(
            "Token", typeof(object), typeof(TileArt),
            new PropertyMetadata(null));

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;

        var token = new object();
        image.SetValue(TokenProperty, token);
        image.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => Update(image, token)));
    }

    private static async void Update(Image image, object token)
    {
        // Superseded by a newer change before we ran? Do nothing.
        if (!ReferenceEquals(image.GetValue(TokenProperty), token)) return;

        image.Source = null;

        var card = GetCard(image);
        if (card is null) return;

        var dataDir = GetDataDirectory(image) ?? "";

        foreach (var candidate in CardArtCandidateResolver.Resolve(card))
        {
            if (candidate.Kind == CardArtKind.Scan)
            {
                var scan = ScanImageCache.Instance?.GetImage(Path.Combine(dataDir, candidate.Value));
                if (scan is not null)
                {
                    if (ReferenceEquals(image.GetValue(TokenProperty), token))
                        image.Source = scan;
                    return;
                }
            }
            else // Downloaded — load off the UI thread
            {
                var cache = CardArtCache.Instance;
                if (cache is null) continue;

                var art = await cache.GetImageAsync(null, candidate.Value);

                // Inputs changed while awaiting: drop this result.
                if (!ReferenceEquals(image.GetValue(TokenProperty), token)) return;

                if (art is not null)
                {
                    image.Source = art;
                    return;
                }
            }
        }
    }
}
