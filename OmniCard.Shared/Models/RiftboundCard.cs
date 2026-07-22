namespace OmniCard.Models;

// Persistence entity for Riftbound cards. One row per printing.
// Populated by RiftboundService from api.riftcodex.com DTOs (see RiftboundApiModels).
public class RiftboundCard
{
    // Riftcodex hex id — unique per printing (alt arts are distinct rows). Primary key.
    public string Id { get; set; } = "";

    // Riftbound id, e.g. "ogn-209-298"; alt arts carry a '*' (e.g. "ogn-310*-298").
    public string RiftboundId { get; set; } = "";
    public string? TcgplayerId { get; set; }

    // Printed collector number, e.g. 150. NOT unique — shared across alt-art printings.
    public int CollectorNumber { get; set; }

    public string Name { get; set; } = "";
    public string? CleanName { get; set; }
    public string SetId { get; set; } = "";      // e.g. "OGN"
    public string SetName { get; set; } = "";     // e.g. "Origins"
    public string Rarity { get; set; } = "";
    public string CardType { get; set; } = "";    // Unit / Spell / Legend / Battlefield
    public string? Supertype { get; set; }
    public string Domain { get; set; } = "";       // domain[] joined with '/', e.g. "Body/Order"
    public int? Energy { get; set; }
    public int? Might { get; set; }
    public int? Power { get; set; }
    public string? CardText { get; set; }
    public string? Flavour { get; set; }
    public string? Artist { get; set; }
    public string Orientation { get; set; } = "portrait"; // "portrait" | "landscape"
    public bool AlternateArt { get; set; }
    public bool Overnumbered { get; set; }
    public bool Signature { get; set; }
    public string? CardImageUri { get; set; }
    public string? DateScraped { get; set; }

    // Computed locally, not from API
    public ulong? ImageHash { get; set; }
    public ulong? EdgeHash { get; set; }
    public string? LocalImagePath { get; set; }
}
