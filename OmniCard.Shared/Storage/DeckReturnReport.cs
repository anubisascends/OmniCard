namespace OmniCard.Shared.Storage;

/// <summary>A saved put-back report: what a deck deconstruction (or a "return to where it came from" on some
/// of a deck box's cards) moved and where each copy went, so the physical cards can be put away and the
/// report printed again later. The lines are stored as JSON (<see cref="LinesJson"/>, a list of
/// <see cref="DeckReturnLine"/>) — they're a snapshot, never queried. Deleted with the deck box.</summary>
public class DeckReturnReport
{
    public int Id { get; set; }
    public int DeckBoxId { get; set; }
    /// <summary>The deck box's name when the cards were returned.</summary>
    public string DeckBoxName { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Who returned the cards (user name), when known.</summary>
    public string? CreatedBy { get; set; }
    /// <summary>True for a whole-deck deconstruction; false when only some cards were returned.</summary>
    public bool WholeDeck { get; set; }
    /// <summary>Total copies returned.</summary>
    public int CardCount { get; set; }
    public string LinesJson { get; set; } = "[]";

    public const int MaxNameLength = 200;
}

/// <summary>Why a copy didn't go back to where it came from (it went to the fallback location or Bulk).</summary>
public enum DeckReturnIssue
{
    /// <summary>It went back where it came from.</summary>
    None,
    /// <summary>No origin is known (added straight into the deck, or there before origins were tracked) —
    /// it went to the fallback location.</summary>
    NoOrigin,
    /// <summary>The location it came from has been deleted — it went to Bulk.</summary>
    LocationGone,
    /// <summary>The location it came from is in a site the user can't edit — it went to Bulk.</summary>
    NoAccess,
    /// <summary>The location it came from now only holds another game's cards — it went to Bulk.</summary>
    WrongGame,
    /// <summary>The binder pocket it came from now holds another card — it went to Bulk.</summary>
    SlotTaken,
}

/// <summary>One line of a put-back report: copies of one card and where to put them. <see cref="Page"/> /
/// <see cref="Slot"/> / <see cref="Section"/> are the position at the destination. When
/// <see cref="Issue"/> isn't <see cref="DeckReturnIssue.None"/>, <see cref="OriginName"/> (and the
/// <c>Origin*</c> position) say where it would have gone and <see cref="SlotTakenBy"/> names the card now
/// in that pocket. <see cref="RejoinedStack"/>: the copies were added to an identical stack already there.</summary>
public sealed record DeckReturnLine
{
    public int LotId { get; init; }
    public string CardName { get; init; } = "";
    public string? SetCode { get; init; }
    public string? CollectorNumber { get; init; }
    public bool IsFoil { get; init; }
    public string? Condition { get; init; }
    public string? Language { get; init; }
    public int Quantity { get; init; }

    public int DestinationId { get; init; }
    public string DestinationName { get; init; } = "";
    public string? DestinationType { get; init; }
    public string? SiteName { get; init; }
    public int? Page { get; init; }
    public int? Slot { get; init; }
    public string? Section { get; init; }
    public bool RejoinedStack { get; init; }

    public DeckReturnIssue Issue { get; init; }
    public string? OriginName { get; init; }
    public int? OriginPage { get; init; }
    public int? OriginSlot { get; init; }
    public string? OriginSection { get; init; }
    public string? SlotTakenBy { get; init; }
}
