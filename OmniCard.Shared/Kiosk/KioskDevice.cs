namespace OmniCard.Shared.Kiosk;

/// <summary>
/// A device an administrator turned into a customer kiosk. The device carries a kiosk-only cookie naming
/// this row; revoking the row (<see cref="RevokedAt"/>) locks the device out on its next request.
/// </summary>
public class KioskDevice
{
    public const int MaxNameLength = 60;

    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
