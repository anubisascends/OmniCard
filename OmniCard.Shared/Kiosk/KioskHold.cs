namespace OmniCard.Shared.Kiosk;

/// <summary>
/// Copies of one lot a kiosk customer put on their pick list. While the session is open the copies are
/// held: no other kiosk can take them, and they don't count as available anywhere the kiosk looks.
/// </summary>
public class KioskHold
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int LotId { get; set; }
    public int Quantity { get; set; } = 1;

    /// <summary>The price shown to the customer when they added it; becomes the order line price.</summary>
    public decimal UnitPrice { get; set; }

    public DateTime CreatedAt { get; set; }
}
