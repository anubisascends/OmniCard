using OmniCard.Api.Contracts;
using OmniCard.CardMatching;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;

namespace OmniCard.Web.Services.TagRules;

/// <summary>Turns unsaved scanned cards into <see cref="CollectionCard"/>s that the in-memory search
/// (<c>CollectionCardMatcher</c>) can test — shared by tag rules and scan validation.</summary>
internal static class ScanRuleCards
{
    /// <summary>One card per item (same order), with type/colour filled from <paramref name="game"/>'s
    /// catalog so <c>t:</c>/<c>c:</c> queries work.</summary>
    public static List<CollectionCard> Build(
        CardGame game, IReadOnlyList<ScanTagRuleItem> items, IReadOnlyDictionary<CardGame, ICardGameService> games)
    {
        var cards = items.Select(i => new CollectionCard
        {
            Game = game,
            GameCardId = i.GameCardId,
            Name = i.Name,
            SetCode = i.SetCode,
            Number = i.CollectorNumber,
            Rarity = i.Rarity,
            Condition = string.IsNullOrWhiteSpace(i.Condition) ? "NM" : i.Condition,
            Language = CardLanguages.Normalize(i.Language) ?? CardLanguages.English,
            IsFoil = i.IsFoil,
            FoilType = i.IsFoil ? i.FoilType : null,
            Tags = (i.Tags ?? []).ToList(),
        }).ToList();

        if (games.TryGetValue(game, out var svc))
            foreach (var card in cards.Where(c => c.GameCardId.Length > 0).DistinctBy(c => c.GameCardId))
            {
                CardAttributeExtractor.FillFromCatalog(card, svc);
                foreach (var twin in cards.Where(c => c.GameCardId == card.GameCardId && !ReferenceEquals(c, card)))
                    (twin.Color, twin.CardType) = (card.Color, card.CardType);
            }
        return cards;
    }
}
