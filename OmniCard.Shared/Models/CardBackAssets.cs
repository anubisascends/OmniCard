using System.Collections.Generic;
using System.Linq;

namespace OmniCard.Models;

/// <summary>Single source of truth for the per-game card-back assets shown in an empty binder pocket
/// whose reverse-side pocket is filled, plus the horizontal-mirror slot math both the desktop and
/// web binder views share. Keeping the game→slug map here (rather than duplicating it in XAML, Razor
/// and JS) stops the desktop and web asset filenames from drifting apart.</summary>
public static class CardBackAssets
{
    /// <summary>Lowercase filename stem for a game's card-back image. Desktop bundles
    /// <c>Resources/CardBacks/{slug}.png</c>; the web app serves <c>/img/card-back-{slug}.png</c>.
    /// The slugs match the per-game DB filenames so they read consistently across the app.</summary>
    public static string Slug(CardGame game) => game switch
    {
        CardGame.Mtg => "mtg",
        CardGame.OnePiece => "optcg",
        CardGame.Riftbound => "riftbound",
        CardGame.Pokemon => "pokemon",
        CardGame.YuGiOh => "yugioh",
        CardGame.FinalFantasy => "fftcg",
        _ => "mtg",
    };

    /// <summary>The slot directly behind <paramref name="slot"/> when the sheet is flipped over —
    /// the same row, but the column mirrored across the grid (front <c>(row, col)</c> ↔ reverse
    /// <c>(row, columns-1-col)</c>), exactly how a physical page reverses. Returns <c>null</c> when
    /// the mirrored index falls outside the page (a ragged final row), so callers can skip it.</summary>
    public static int? MirrorSlot(int slot, int columns, int slotsPerPage)
    {
        if (columns <= 0 || slot < 0 || slot >= slotsPerPage) return null;
        var row = slot / columns;
        var col = slot % columns;
        var mirrored = row * columns + (columns - 1 - col);
        return mirrored >= 0 && mirrored < slotsPerPage ? mirrored : null;
    }

    /// <summary>The card sitting in the pocket directly behind an <em>empty</em> pocket at
    /// <paramref name="slot"/> — i.e. the card in the horizontally-mirrored pocket
    /// (<see cref="MirrorSlot"/>) on the reverse side of the physical sheet — or <c>null</c> if that
    /// mirrored pocket is empty or off-page. <paramref name="reverseCards"/> are the placed cards on
    /// the reverse logical page (see <c>BinderSheetLayout.ReversePageOf</c>). The single seam the
    /// desktop binder and both web binder views share to decide whether to show a card back.</summary>
    public static CollectionCard? ReverseCardFor(
        int slot, int columns, int slotsPerPage, IReadOnlyList<CollectionCard> reverseCards)
        => MirrorSlot(slot, columns, slotsPerPage) is int mirrored
            ? reverseCards.FirstOrDefault(c => c.Slot == mirrored)
            : null;
}
