using OmniCard.Models;

namespace OmniCard.CardMatching;

public static class CardAttributeExtractor
{
    private static readonly string[] MtgTypePriority =
    [
        "Legendary Creature",
        "Creature",
        "Instant",
        "Sorcery",
        "Enchantment",
        "Artifact",
        "Planeswalker",
        "Land"
    ];

    public static string? ExtractColor(CardMatch match, CardGame game)
    {
        return game switch
        {
            CardGame.Mtg => ExtractMtgColor(match.Source as Card),
            CardGame.OnePiece => (match.Source as OptcgCard)?.CardColor,
            CardGame.Riftbound => (match.Source as RiftboundCard)?.Domain,
            CardGame.Pokemon => (match.Source as TcgCsvCard)?.CardType,
            CardGame.YuGiOh => (match.Source as TcgCsvCard)?.CardType,
            CardGame.FinalFantasy => (match.Source as TcgCsvCard)?.CardType,
            _ => null
        };
    }

    public static string? ExtractCardType(CardMatch match, CardGame game)
    {
        return game switch
        {
            CardGame.Mtg => ExtractMtgCardType(match.Source as Card),
            CardGame.OnePiece => (match.Source as OptcgCard)?.CardType,
            CardGame.Riftbound => (match.Source as RiftboundCard)?.CardType,
            CardGame.Pokemon => (match.Source as TcgCsvCard)?.CardType,
            CardGame.YuGiOh => (match.Source as TcgCsvCard)?.CardType,
            CardGame.FinalFantasy => (match.Source as TcgCsvCard)?.CardType,
            _ => null
        };
    }

    private static string ExtractMtgColor(Card? card)
    {
        if (card is null)
            return "Colorless";

        var colors = card.Colors;
        if (colors is null || colors.Count == 0)
        {
            return card.TypeLine.Contains("Land", StringComparison.OrdinalIgnoreCase)
                ? "Land"
                : "Colorless";
        }

        return string.Join("", colors);
    }

    private static string? ExtractMtgCardType(Card? card)
    {
        if (card is null)
            return null;

        var typeLine = card.TypeLine;
        foreach (var type in MtgTypePriority)
        {
            if (typeLine.Contains(type, StringComparison.OrdinalIgnoreCase))
                return type;
        }

        return typeLine;
    }
}
