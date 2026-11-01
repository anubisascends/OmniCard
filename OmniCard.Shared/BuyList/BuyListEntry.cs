using OmniCard.Shared.Cards;

namespace OmniCard.Shared.BuyList;

/// <summary>How much a Buy List card matters; the higher ones get a stronger marker and sort first.</summary>
public enum BuyListPriority
{
    Normal,
    High,
    Grail,
}

/// <summary>
/// One card the collection's owner wants to buy. Each game has exactly one Buy List (the entries of that
/// <see cref="Game"/>), edited only by administrators. By default an entry matches every printing of the
/// card (by <see cref="MatchName"/>); <see cref="PinPrinting"/> narrows it to the printing it was added from
/// and <see cref="Foil"/> to one finish. <see cref="WantedQuantity"/> counts down as copies are bought or
/// scanned in; at 0 the entry is fulfilled, so it stops flagging and leaves the kiosk, but stays on file.
/// </summary>
public class BuyListEntry
{
    public const int MaxNameLength = 300;
    public const int MaxNoteLength = 500;
    public const int MaxQuantity = 9999;

    public int Id { get; set; }

    public CardGame Game { get; set; }

    /// <summary>The name of the printing it was added from.</summary>
    public string Name { get; set; } = "";

    /// <summary><see cref="Name"/> without printing details (see <see cref="BuyListMatcher.BaseName"/>).</summary>
    public string MatchName { get; set; } = "";

    /// <summary>The printing it was added from. Gives the entry its art and label; it only limits matching
    /// when <see cref="PinPrinting"/> is on.</summary>
    public string GameCardId { get; set; } = "";
    public string? SetCode { get; set; }
    public string? SetName { get; set; }
    public string? CollectorNumber { get; set; }
    public string? ImageUri { get; set; }

    /// <summary>Match only <see cref="GameCardId"/> rather than any printing.</summary>
    public bool PinPrinting { get; set; }

    /// <summary>True = foil copies only, false = non-foil only, null = either.</summary>
    public bool? Foil { get; set; }

    /// <summary>Copies still wanted.</summary>
    public int WantedQuantity { get; set; } = 1;

    /// <summary>Copies bought or scanned in against this entry so far.</summary>
    public int AcquiredQuantity { get; set; }

    public BuyListPriority Priority { get; set; }

    /// <summary>Staff-only note. Never shown to customers.</summary>
    public string? Note { get; set; }

    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
