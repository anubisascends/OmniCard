namespace OmniCard.Shared.Intake;

/// <summary>Which catalog price an intake quote is built on.</summary>
public enum IntakePriceBasis
{
    /// <summary>TCGplayer market price.</summary>
    Market,
    /// <summary>TCGplayer low listing price (falls back to market when a card has no low).</summary>
    Low,
}

/// <summary>How a card worth less than <see cref="IntakeSettings.BulkThreshold"/> is handled.</summary>
public enum IntakeBulkMode
{
    /// <summary>Offer the flat <see cref="IntakeSettings.BulkRate"/> per card, cash or trade.</summary>
    FlatRate,
    /// <summary>Leave the card out of the offer by default (staff can still add it back).</summary>
    Exclude,
}

/// <summary>Whether offers are rounded card by card or only on the totals.</summary>
public enum IntakeRoundingScope
{
    PerCard,
    Total,
}

/// <summary>How a bought card is paid for.</summary>
public enum IntakePayout
{
    Cash,
    /// <summary>Store credit on the customer's account.</summary>
    Trade,
}

/// <summary>What the seller sees on the customer view of a quote (offer totals are always shown).</summary>
public sealed class IntakeCustomerView
{
    public bool ShowCardImages { get; set; } = true;
    /// <summary>Set, number, condition and finish under each card name.</summary>
    public bool ShowCardDetails { get; set; } = true;
    /// <summary>Each card's market (or low) price.</summary>
    public bool ShowCardPrices { get; set; } = true;
    /// <summary>Each card's cash / trade offer.</summary>
    public bool ShowCardOffers { get; set; } = true;
    /// <summary>The grand total market value.</summary>
    public bool ShowMarketTotal { get; set; } = true;
}

/// <summary>Admin settings for buying singles over the counter (<c>intake-settings.json</c> in the data
/// directory): the cash and trade percentages of the card's price, condition adjustments, bulk handling,
/// rounding, where bought cards go and what the seller is shown.</summary>
public sealed class IntakeSettings
{
    public const decimal MaxPercent = 200m;
    public static readonly string[] Conditions = ["NM", "LP", "MP", "HP", "DMG"];
    public static readonly decimal[] RoundingSteps = [0m, 0.05m, 0.10m, 0.25m, 0.50m, 1m];

    /// <summary>Cash offer as a percentage of the (condition-adjusted) price.</summary>
    public decimal CashPercent { get; set; } = 70m;

    /// <summary>Trade (store credit) offer as a percentage of the (condition-adjusted) price.</summary>
    public decimal TradePercent { get; set; } = 80m;

    /// <summary>Percent of the price paid per condition code (NM, LP, MP, HP, DMG). Missing = 100.</summary>
    public Dictionary<string, decimal> ConditionPercents { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NM"] = 100m, ["LP"] = 85m, ["MP"] = 70m, ["HP"] = 50m, ["DMG"] = 30m,
    };

    /// <summary>Cards whose condition-adjusted price is below this count as bulk. 0 = no bulk rule.</summary>
    public decimal BulkThreshold { get; set; } = 0.25m;

    public IntakeBulkMode BulkMode { get; set; } = IntakeBulkMode.FlatRate;

    /// <summary>Per-card offer for bulk under <see cref="IntakeBulkMode.FlatRate"/> (cash and trade alike).</summary>
    public decimal BulkRate { get; set; } = 0.01m;

    /// <summary>Offers round DOWN to a multiple of this (one of <see cref="RoundingSteps"/>; 0 = whole cents).</summary>
    public decimal RoundingStep { get; set; }

    public IntakeRoundingScope RoundingScope { get; set; } = IntakeRoundingScope.Total;

    /// <summary>The storage location bought cards are put into. Null = intake can quote but not buy.</summary>
    public int? LocationId { get; set; }

    public IntakeCustomerView CustomerView { get; set; } = new();
}

/// <summary>Reads and writes <see cref="IntakeSettings"/>.</summary>
public interface IIntakeSettingsService
{
    IntakeSettings Get();
    void Save(IntakeSettings settings);
}
