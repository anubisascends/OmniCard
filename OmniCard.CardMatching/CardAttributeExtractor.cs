using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Matching;

namespace OmniCard.CardMatching;

public static class CardAttributeExtractor
{
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

    // Store the FULL type line ("Legendary Creature — Vampire"), not a collapsed bucket. Subtypes and
    // supertypes live only in the full line, so collapsing to "Creature" would make t:vampire / t:legendary
    // unmatchable in owned-collection search. Callers that want a coarse primary type derive it on demand.
    private static string? ExtractMtgCardType(Card? card) => card?.TypeLine;
}
