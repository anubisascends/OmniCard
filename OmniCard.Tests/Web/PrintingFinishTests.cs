using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

public class PrintingFinishTests
{
    [Theory]
    [InlineData(new[] { "nonfoil", "foil" }, null, null)]
    [InlineData(new[] { "foil" }, true, null)]
    [InlineData(new[] { "etched" }, true, "Etched")]
    [InlineData(new[] { "foil", "etched" }, true, null)]
    [InlineData(new[] { "nonfoil" }, false, null)]
    [InlineData(new[] { "nonfoil", "etched" }, null, null)]
    [InlineData(new string[0], null, null)]
    public void Mtg_reads_scryfall_finishes(string[] finishes, bool? foil, string? foilType)
    {
        var (f, type) = PrintingFinish.Of(new Card { Finishes = [.. finishes] });
        Assert.Equal(foil, f);
        Assert.Equal(foilType, type);
    }

    [Fact]
    public void TcgCsv_foil_price_without_normal_price_is_foil_only()
    {
        Assert.Equal((true, null), PrintingFinish.Of(new TcgCsvCard { Game = CardGame.Pokemon, FoilMarketPrice = 3m }));
        Assert.Equal((null, null), PrintingFinish.Of(new TcgCsvCard { Game = CardGame.Pokemon, MarketPrice = 1m, FoilMarketPrice = 3m }));
        // Only a normal price, or none at all, says nothing (foils may just be unpriced).
        Assert.Equal((null, null), PrintingFinish.Of(new TcgCsvCard { Game = CardGame.Pokemon, MarketPrice = 1m }));
        Assert.Equal((null, null), PrintingFinish.Of(new TcgCsvCard { Game = CardGame.Pokemon }));
    }

    [Fact]
    public void YuGiOh_subtypes_are_editions_not_finishes()
    {
        Assert.Equal((null, null), PrintingFinish.Of(new TcgCsvCard { Game = CardGame.YuGiOh, FoilMarketPrice = 3m }));
    }

    [Fact]
    public void Riftbound_foil_price_without_normal_price_is_foil_only()
    {
        Assert.Equal((true, null), PrintingFinish.Of(new RiftboundCard { FoilLowPrice = 2m }));
        Assert.Equal((null, null), PrintingFinish.Of(new RiftboundCard { LowPrice = 1m, FoilLowPrice = 2m }));
    }

    [Fact]
    public void Unknown_source_says_nothing()
    {
        Assert.Equal((null, null), PrintingFinish.Of(null));
        Assert.Equal((null, null), PrintingFinish.Of(new OptcgCard()));
    }
}
