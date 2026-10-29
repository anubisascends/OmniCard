using OmniCard.Shared.Intake;
using OmniCard.Web.Services.Intake;

namespace OmniCard.Tests.Web.Intake;

/// <summary>The intake pricing rules: basis price, condition percentage, bulk, percentages and rounding.</summary>
public class IntakePricingTests
{
    private static IntakeSettings Settings(Action<IntakeSettings>? tweak = null)
    {
        var s = new IntakeSettings { BulkThreshold = 0 };
        IntakeSettingsService.Normalize(s);
        tweak?.Invoke(s);
        return s;
    }

    private static IntakePricingInput Card(decimal? market, decimal? low = null, string condition = "NM",
        int qty = 1, bool? excluded = null, IntakePayout payout = IntakePayout.Cash, decimal? manual = null) =>
        new(market, low, manual, condition, qty, excluded, payout);

    [Fact]
    public void Market_Basis_Pays_Cash_And_Trade_Percentages()
    {
        var line = IntakePricing.PriceLine(Card(10m), Settings(), IntakePriceBasis.Market);

        Assert.Equal(10m, line.BasePrice);
        Assert.Equal(7m, line.CashUnit);
        Assert.Equal(8m, line.TradeUnit);
        Assert.False(line.NeedsPrice);
    }

    [Fact]
    public void Low_Basis_Uses_Low_And_Falls_Back_To_Market()
    {
        var withLow = IntakePricing.PriceLine(Card(10m, low: 6m), Settings(), IntakePriceBasis.Low);
        var noLow = IntakePricing.PriceLine(Card(10m), Settings(), IntakePriceBasis.Low);

        Assert.Equal(6m, withLow.BasePrice);
        Assert.False(withLow.UsedMarketFallback);
        Assert.Equal(10m, noLow.BasePrice);
        Assert.True(noLow.UsedMarketFallback);
    }

    [Fact]
    public void Condition_Percentage_Applies_Before_The_Offer()
    {
        var line = IntakePricing.PriceLine(Card(10m, condition: "LP"), Settings(), IntakePriceBasis.Market);

        Assert.Equal(85m, line.ConditionPercent);
        Assert.Equal(5.95m, line.CashUnit);   // 10 × 85% × 70%
        Assert.Equal(6.8m, line.TradeUnit);   // 10 × 85% × 80%
    }

    [Fact]
    public void Manual_Price_Replaces_The_Catalog_Price()
    {
        var line = IntakePricing.PriceLine(Card(null, manual: 4m), Settings(), IntakePriceBasis.Market);

        Assert.True(line.IsManualPrice);
        Assert.Equal(4m, line.BasePrice);
        Assert.Equal(2.8m, line.CashUnit);
    }

    [Fact]
    public void No_Price_Needs_One_Unless_Excluded()
    {
        var settings = Settings();
        Assert.True(IntakePricing.PriceLine(Card(null), settings, IntakePriceBasis.Market).NeedsPrice);
        Assert.False(IntakePricing.PriceLine(Card(null, excluded: true), settings, IntakePriceBasis.Market).NeedsPrice);
    }

    [Fact]
    public void Bulk_FlatRate_Pays_The_Rate_For_Cash_And_Trade()
    {
        var settings = Settings(s => { s.BulkThreshold = 0.25m; s.BulkRate = 0.02m; });
        var line = IntakePricing.PriceLine(Card(0.20m), settings, IntakePriceBasis.Market);

        Assert.True(line.IsBulk);
        Assert.False(line.Excluded);
        Assert.Equal(0.02m, line.CashUnit);
        Assert.Equal(0.02m, line.TradeUnit);
    }

    [Fact]
    public void Bulk_Threshold_Is_Checked_After_Condition()
    {
        // $0.40 HP card (50%) is worth $0.20 — under a $0.25 threshold.
        var settings = Settings(s => s.BulkThreshold = 0.25m);
        Assert.True(IntakePricing.PriceLine(Card(0.40m, condition: "HP"), settings, IntakePriceBasis.Market).IsBulk);
        Assert.False(IntakePricing.PriceLine(Card(0.40m), settings, IntakePriceBasis.Market).IsBulk);
    }

    [Fact]
    public void Bulk_Exclude_Starts_Excluded_But_Staff_Can_Include()
    {
        var settings = Settings(s => { s.BulkThreshold = 0.25m; s.BulkMode = IntakeBulkMode.Exclude; });

        Assert.True(IntakePricing.PriceLine(Card(0.10m), settings, IntakePriceBasis.Market).Excluded);
        var included = IntakePricing.PriceLine(Card(0.10m, excluded: false), settings, IntakePriceBasis.Market);
        Assert.False(included.Excluded);
        Assert.Equal(0.07m, included.CashUnit);
    }

    [Fact]
    public void PerCard_Rounding_Floors_Each_Unit_To_The_Step()
    {
        var settings = Settings(s => { s.RoundingScope = IntakeRoundingScope.PerCard; s.RoundingStep = 0.25m; });
        var line = IntakePricing.PriceLine(Card(3.33m), settings, IntakePriceBasis.Market);

        Assert.Equal(2.25m, line.CashUnit);   // 2.331 ↓ 0.25
        Assert.Equal(2.50m, line.TradeUnit);  // 2.664 ↓ 0.25
    }

    [Fact]
    public void Total_Rounding_Floors_Only_The_Totals()
    {
        var settings = Settings(s => { s.RoundingScope = IntakeRoundingScope.Total; s.RoundingStep = 1m; });
        var inputs = new[] { Card(3.33m), Card(3.33m) };
        var lines = inputs.Select(i => (i, IntakePricing.PriceLine(i, settings, IntakePriceBasis.Market))).ToList();

        var totals = IntakePricing.Totals(lines, settings);

        Assert.Equal(4m, totals.CashOffer);   // 4.662 ↓ 1
        Assert.Equal(5m, totals.TradeOffer);  // 5.328 ↓ 1
    }

    [Fact]
    public void Totals_Split_Payout_And_Skip_Excluded_Lines()
    {
        var settings = Settings();
        var inputs = new[]
        {
            Card(10m, qty: 2),                                  // cash 14, trade 16
            Card(5m, payout: IntakePayout.Trade),               // cash 3.5, trade 4
            Card(100m, excluded: true),                         // left out
        };
        var lines = inputs.Select(i => (i, IntakePricing.PriceLine(i, settings, IntakePriceBasis.Market))).ToList();

        var totals = IntakePricing.Totals(lines, settings);

        Assert.Equal(3, totals.CardCount);
        Assert.Equal(25m, totals.MarketTotal);
        Assert.Equal(17.5m, totals.CashOffer);
        Assert.Equal(20m, totals.TradeOffer);
        Assert.Equal(14m, totals.CashPaid);
        Assert.Equal(4m, totals.TradePaid);
    }

    [Theory]
    [InlineData(2.339, 0, 2.33)]
    [InlineData(2.339, 0.05, 2.30)]
    [InlineData(2.339, 0.10, 2.30)]
    [InlineData(2.75, 0.50, 2.50)]
    public void FloorTo_Rounds_Down_To_The_Step(decimal value, decimal step, decimal expected) =>
        Assert.Equal(expected, IntakePricing.FloorTo(value, step));
}
