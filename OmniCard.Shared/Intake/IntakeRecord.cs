using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Intake;

/// <summary>
/// One accepted buy of singles from a seller — the audit trail of who took what in, from whom, and at
/// what price. Written once when staff accept a quote and never edited. The quote's inputs (percentages,
/// price basis and the settings snapshot) are kept so the amounts can be explained later even after the
/// admin changes the settings.
/// </summary>
public class IntakeRecord
{
    public const int MaxNoteLength = 500;

    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>The staff member who accepted the intake.</summary>
    public int? CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";

    /// <summary>The seller (required when any card is paid in trade credit).</summary>
    public int? CustomerId { get; set; }
    /// <summary>The seller's name at the time (kept if the customer is later renamed or deleted).</summary>
    public string? CustomerName { get; set; }

    public IntakePriceBasis PriceBasis { get; set; }
    public decimal CashPercent { get; set; }
    public decimal TradePercent { get; set; }

    /// <summary>Where the cards were put, with its name at the time.</summary>
    public int LocationId { get; set; }
    public string LocationName { get; set; } = "";

    /// <summary>Copies bought (sum of line quantities).</summary>
    public int CardCount { get; set; }
    /// <summary>Total market value of the bought copies.</summary>
    public decimal MarketTotal { get; set; }
    /// <summary>Cash paid out.</summary>
    public decimal CashPaid { get; set; }
    /// <summary>Store credit issued.</summary>
    public decimal TradePaid { get; set; }

    public string? Note { get; set; }

    /// <summary>The intake settings in force (JSON of <see cref="IntakeSettings"/>), for the audit.</summary>
    public string SettingsJson { get; set; } = "{}";
}

/// <summary>One bought card (or stack of identical copies) on an <see cref="IntakeRecord"/>.</summary>
public class IntakeLine
{
    public int Id { get; set; }
    public int IntakeId { get; set; }

    public CardGame Game { get; set; }
    public string GameCardId { get; set; } = "";
    public string Name { get; set; } = "";
    public string SetCode { get; set; } = "";
    public string SetName { get; set; } = "";
    public string CollectorNumber { get; set; } = "";
    public bool IsFoil { get; set; }
    public string? FoilType { get; set; }
    public string Condition { get; set; } = "NM";
    public string? Language { get; set; }
    public int Quantity { get; set; } = 1;

    /// <summary>Catalog prices per copy at the time (null = the catalog had none).</summary>
    public decimal? MarketPrice { get; set; }
    public decimal? LowPrice { get; set; }
    /// <summary>The per-copy price the offer was built on (basis price, or the staff-entered one).</summary>
    public decimal BasePrice { get; set; }
    /// <summary>Staff typed <see cref="BasePrice"/> because the catalog had no price.</summary>
    public bool IsManualPrice { get; set; }
    public decimal ConditionPercent { get; set; }
    public bool IsBulk { get; set; }

    public IntakePayout Payout { get; set; }
    /// <summary>Paid per copy (cash, or trade credit, per <see cref="Payout"/>).</summary>
    public decimal UnitPaid { get; set; }

    /// <summary>The inventory lot the copies became.</summary>
    public int? LotId { get; set; }
}
