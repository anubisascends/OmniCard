using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Web;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>Unit tests for the web app's presentation helpers (TCGplayer deep-linking + live market
/// price hydration). These outlived the retired Razor pages — they back the SPA API controllers.</summary>
public class WebHelperTests
{
    private static readonly ICardService NoGameServices = new WebCardService([]);

    // --- TcgPlayerLink ---

    [Fact]
    public void TcgPlayerLink_NumericGameCardId_DeepLinksToProduct()
    {
        // TCGCSV games (Pokémon/Yu-Gi-Oh!/FFTCG/Riftbound) store the real TCGplayer product id.
        var url = TcgPlayerLink.Build(CardGame.Pokemon, "12345", "Pikachu", "Base Set");
        Assert.Equal("https://www.tcgplayer.com/product/12345", url);
    }

    [Fact]
    public void TcgPlayerLink_ResolvedProductId_DeepLinksToProduct()
    {
        // MTG stores a Scryfall GUID; the real product id is resolved and passed in explicitly.
        var url = TcgPlayerLink.Build(
            CardGame.Mtg, "9e1a...-guid", "Lightning Bolt", "Alpha", resolvedProductId: 987);
        Assert.Equal("https://www.tcgplayer.com/product/987", url);
    }

    [Fact]
    public void TcgPlayerLink_NonNumericId_NoResolution_FallsBackToScopedSearch()
    {
        // One Piece stores a set code (e.g. OP01-001) and has no TCGplayer id → search.
        var url = TcgPlayerLink.Build(CardGame.OnePiece, "OP01-001", "Monkey D. Luffy", "Romance Dawn");

        Assert.StartsWith("https://www.tcgplayer.com/search/one-piece-card-game/product?q=", url);
        Assert.Contains(Uri.EscapeDataString("Monkey D. Luffy Romance Dawn"), url);
    }

    [Fact]
    public void TcgPlayerLink_SearchWithoutSet_UsesNameOnly()
    {
        var url = TcgPlayerLink.Build(CardGame.OnePiece, "OP01-001", "Nami", setName: null);
        Assert.EndsWith("product?q=" + Uri.EscapeDataString("Nami"), url);
    }

    // --- MarketPriceHydrator ---

    [Fact]
    public void MarketPriceHydrator_Populate_SetsLivePricePerFoilGroup()
    {
        // MTG catalog returns different prices for foil vs non-foil printings of the same id.
        var game = new OmniCard.Tests.Fakes.ConfigurableGameService
        {
            OnGetCurrentPrices = (ids, foil) => ids.ToDictionary(id => id, _ => foil ? 20m : 5m),
        };
        var cardService = new WebCardService([game]);

        var plain = new CollectionCard { Game = CardGame.Mtg, GameCardId = "bolt", IsFoil = false };
        var foilCard = new CollectionCard { Game = CardGame.Mtg, GameCardId = "bolt", IsFoil = true };
        var traded = new CollectionCard { Game = CardGame.Mtg, GameCardId = "bolt", IsFoil = false, IsTraded = true };

        MarketPriceHydrator.Populate(cardService, [plain, foilCard, traded]);

        Assert.Equal(5m, plain.MarketPrice);
        Assert.Equal(20m, foilCard.MarketPrice);
        Assert.Equal(0m, traded.MarketPrice); // traded cards are excluded from live pricing
    }

    [Fact]
    public void MarketPriceHydrator_Populate_UnregisteredGame_LeavesPriceZero()
    {
        // No game services registered → GetGameService throws → cards keep 0 rather than blowing up.
        var card = new CollectionCard { Game = CardGame.Mtg, GameCardId = "x", MarketPrice = 0m };
        MarketPriceHydrator.Populate(NoGameServices, [card]);
        Assert.Equal(0m, card.MarketPrice);
    }
}
