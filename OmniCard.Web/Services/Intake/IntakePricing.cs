using OmniCard.Shared.Intake;

namespace OmniCard.Web.Services.Intake;

/// <summary>One card on a quote, as the calculator needs it: the catalog prices per copy, the staff's
/// choices, how many identical copies, and how many of them are still wanted on the Buy List.</summary>
public sealed record IntakePricingInput(
    decimal? MarketPrice,
    decimal? LowPrice,
    decimal? ManualPrice,
    string? Condition,
    int Quantity,
    bool? Excluded,
    IntakePayout Payout,
    int BuyListCopies = 0);

/// <summary>A priced quote line. Unit amounts are per copy: <see cref="BuyListCopies"/> of the line pay the
/// Buy List units, the rest the ordinary ones.</summary>
public sealed record IntakePricedLine(
    // The per-copy price the offer is built on; null = no price yet (staff must enter one).
    decimal? BasePrice,
    bool IsManualPrice,
    // Low basis was asked for but the card has no low price, so market was used.
    bool UsedMarketFallback,
    decimal ConditionPercent,
    bool IsBulk,
    bool Excluded,
    decimal CashUnit,
    decimal TradeUnit,
    int BuyListCopies = 0,
    decimal BuyListCashUnit = 0m,
    decimal BuyListTradeUnit = 0m)
{
    public bool NeedsPrice => BasePrice is null && !Excluded;
    public decimal PaidUnit(IntakePayout payout) => payout == IntakePayout.Cash ? CashUnit : TradeUnit;
    public decimal BuyListPaidUnit(IntakePayout payout) => payout == IntakePayout.Cash ? BuyListCashUnit : BuyListTradeUnit;

    /// <summary>The whole line at the cash offer.</summary>
    public decimal CashTotal(int quantity) => CashUnit * (quantity - BuyListCopies) + BuyListCashUnit * BuyListCopies;

    /// <summary>The whole line at the trade offer.</summary>
    public decimal TradeTotal(int quantity) => TradeUnit * (quantity - BuyListCopies) + BuyListTradeUnit * BuyListCopies;
}

/// <summary>Quote totals over the included lines.</summary>
public sealed record IntakeTotals(
    int CardCount,
    // Market value of the included copies (a staff-entered price stands in where there's none).
    decimal MarketTotal,
    // What everything would pay as cash / as trade credit — the two offers shown to the seller.
    decimal CashOffer,
    decimal TradeOffer,
    // What the per-card cash/trade choices actually pay out.
    decimal CashPaid,
    decimal TradePaid);

/// <summary>
/// The intake pricing rules, in order: pick the base price (staff-entered, else the basis price — low
/// falling back to market), apply the condition percentage, then either the bulk rule (below the
/// threshold: flat rate, or left out by default) or the cash/trade percentages. Rounding is always DOWN
/// (in the store's favor) to the configured step — per card, or only on the totals. A card still wanted on
/// the Buy List is never bulk, and its wanted copies pay the Buy List percentages where those are set.
/// </summary>
public static class IntakePricing
{
    public static IntakePricedLine PriceLine(IntakePricingInput input, IntakeSettings settings, IntakePriceBasis basis)
    {
        var usedFallback = false;
        decimal? basePrice;
        var manual = input.ManualPrice is >= 0;
        if (manual)
            basePrice = input.ManualPrice;
        else if (basis == IntakePriceBasis.Low)
        {
            basePrice = input.LowPrice ?? input.MarketPrice;
            usedFallback = input.LowPrice is null && input.MarketPrice is not null;
        }
        else
            basePrice = input.MarketPrice;

        var conditionPercent = settings.ConditionPercents.TryGetValue(input.Condition ?? "NM", out var pct) ? pct : 100m;
        var adjusted = basePrice * conditionPercent / 100m;
        var buyListCopies = Math.Clamp(input.BuyListCopies, 0, Math.Max(1, input.Quantity));
        var isBulk = buyListCopies == 0 && adjusted is not null && settings.BulkThreshold > 0 && adjusted < settings.BulkThreshold;
        var excluded = input.Excluded ?? (isBulk && settings.BulkMode == IntakeBulkMode.Exclude);

        decimal cashUnit = 0, tradeUnit = 0, buyListCash = 0, buyListTrade = 0;
        if (adjusted is { } price)
        {
            if (isBulk && settings.BulkMode == IntakeBulkMode.FlatRate)
                cashUnit = tradeUnit = settings.BulkRate;
            else
            {
                cashUnit = Unit(price * settings.CashPercent / 100m, settings);
                tradeUnit = Unit(price * settings.TradePercent / 100m, settings);
            }
            if (buyListCopies > 0)
            {
                buyListCash = Unit(price * (settings.BuyListCashPercent ?? settings.CashPercent) / 100m, settings);
                buyListTrade = Unit(price * (settings.BuyListTradePercent ?? settings.TradePercent) / 100m, settings);
            }
        }

        return new IntakePricedLine(basePrice, manual, usedFallback, conditionPercent, isBulk, excluded, cashUnit, tradeUnit,
            buyListCopies, buyListCash, buyListTrade);
    }

    public static IntakeTotals Totals(
        IReadOnlyList<(IntakePricingInput Input, IntakePricedLine Priced)> lines, IntakeSettings settings)
    {
        int count = 0;
        decimal market = 0, cash = 0, trade = 0, cashPaid = 0, tradePaid = 0;
        foreach (var (input, priced) in lines)
        {
            if (priced.Excluded) continue;
            var qty = Math.Max(1, input.Quantity);
            count += qty;
            market += (input.MarketPrice ?? (priced.IsManualPrice ? priced.BasePrice : null) ?? 0m) * qty;
            cash += priced.CashTotal(qty);
            trade += priced.TradeTotal(qty);
            if (input.Payout == IntakePayout.Cash) cashPaid += priced.CashTotal(qty);
            else tradePaid += priced.TradeTotal(qty);
        }

        // Per-card rounding already made every unit a multiple of the step; total rounding happens here.
        decimal Total(decimal value) => settings.RoundingScope == IntakeRoundingScope.Total
            ? FloorTo(value, settings.RoundingStep)
            : FloorTo(value, 0m);
        return new IntakeTotals(count, Math.Round(market, 2), Total(cash), Total(trade), Total(cashPaid), Total(tradePaid));
    }

    // Per-card scope rounds each unit offer down to the step; total scope keeps sub-cent precision so the
    // totals aren't skewed by many per-card truncations before they're rounded once.
    private static decimal Unit(decimal value, IntakeSettings settings) =>
        settings.RoundingScope == IntakeRoundingScope.PerCard
            ? FloorTo(value, settings.RoundingStep)
            : Math.Round(value, 4, MidpointRounding.ToZero);

    /// <summary>Rounds down to a multiple of <paramref name="step"/> (0 = whole cents).</summary>
    public static decimal FloorTo(decimal value, decimal step)
    {
        if (step <= 0) step = 0.01m;
        return Math.Floor(value / step) * step;
    }
}
