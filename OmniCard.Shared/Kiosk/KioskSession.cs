namespace OmniCard.Shared.Kiosk;

/// <summary>Where a customer's kiosk pick list is in its life.</summary>
public enum KioskSessionStatus
{
    /// <summary>The customer is still at the kiosk, adding cards.</summary>
    Browsing,
    /// <summary>Sent to the counter; waiting for staff to pull the cards.</summary>
    Queued,
    /// <summary>A staff member claimed the list and is pulling the cards.</summary>
    Picking,
    /// <summary>Turned into an order (<see cref="KioskSession.OrderId"/>).</summary>
    Completed,
    /// <summary>Abandoned by the customer or cancelled by staff.</summary>
    Cancelled,
    /// <summary>Released after the kiosk idle timeout while still browsing.</summary>
    Expired,
}

/// <summary>
/// One customer's visit to a kiosk: the name they gave plus the cards they held (<see cref="KioskHold"/>).
/// The name is unique among open sessions (<see cref="ActiveNameKey"/>) so staff can call the customer
/// to the counter by it. Open = Browsing, Queued or Picking; closing a session releases its holds.
/// </summary>
public class KioskSession
{
    public const int MaxNameLength = 40;

    public int Id { get; set; }

    /// <summary>The name the customer typed (trimmed, as entered).</summary>
    public string CustomerName { get; set; } = "";

    /// <summary>Normalized <see cref="CustomerName"/> while the session is open, null once closed. A unique
    /// index on it is what makes names unique among open sessions only.</summary>
    public string? ActiveNameKey { get; set; }

    /// <summary>The kiosk the customer used (<see cref="KioskDevice"/>).</summary>
    public int DeviceId { get; set; }

    public KioskSessionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Last customer interaction; a Browsing session idle past the timeout expires.</summary>
    public DateTime LastActivityAt { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    /// <summary>The staff member pulling the cards (set while Picking).</summary>
    public int? ClaimedByUserId { get; set; }
    public string? ClaimedByName { get; set; }

    /// <summary>The order the list became (Completed sessions).</summary>
    public int? OrderId { get; set; }

    public bool IsOpen => Status is KioskSessionStatus.Browsing or KioskSessionStatus.Queued or KioskSessionStatus.Picking;

    /// <summary>The uniqueness key for a customer name: trimmed, inner whitespace collapsed, upper-cased.</summary>
    public static string NameKey(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
