using OmniCard.Shared.Binder;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
namespace OmniCard.Shared.Storage;

public class StorageContainer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ContainerType ContainerType { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public int? CoverCardId { get; set; }
    public bool ExcludeFromDeckCheck { get; set; }

    /// <summary>The <see cref="Sites.Site"/> (major physical location — a home, a shop) this location
    /// sits in. Defaults to the seeded default site; the system Bulk location always stays there.
    /// Controls who can see/edit this location and its cards (see <see cref="Sites.SiteAccessGrant"/>).</summary>
    public int SiteId { get; set; } = Sites.Site.DefaultSiteId;

    /// <summary>The game system this location holds, or null for any game. A location with a game
    /// rejects cards from other games on every card-in path (see <c>LocationGameGuard</c>). Required for
    /// a <see cref="ContainerType.DeckBox"/> (null only on legacy deck boxes); optional on binders, boxes
    /// and display cases; never set on the system Bulk location.</summary>
    public CardGame? Game { get; set; }

    /// <summary>Comma-separated set codes (catalog spelling, e.g. <c>"msh,msc,mar"</c>) that card searches
    /// for this location start limited to — the binder edit Sets filter and the location's Add card. Only
    /// a default: it is never enforced, and the user can search every set. Only kept while
    /// <see cref="Game"/> is set (the codes are that game's). Null = no default sets.</summary>
    public string? DefaultSets { get; set; }

    /// <summary>Longest <see cref="DefaultSets"/> accepted.</summary>
    public const int MaxDefaultSetsLength = 1000;

    /// <summary><see cref="DefaultSets"/> as a list (empty when unset).</summary>
    public IReadOnlyList<string> DefaultSetCodes => ParseSetCodes(DefaultSets);

    /// <summary>Splits a stored <see cref="DefaultSets"/> value into its codes.</summary>
    public static IReadOnlyList<string> ParseSetCodes(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Normalizes set codes for <see cref="DefaultSets"/>: trimmed, blanks dropped, de-duplicated
    /// case-insensitively (first spelling kept); null when none are left.</summary>
    public static string? JoinSetCodes(IEnumerable<string>? codes)
    {
        var list = (codes ?? [])
            .Select(c => (c ?? "").Trim().Replace(",", ""))
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return list.Count == 0 ? null : string.Join(",", list);
    }

    /// <summary>FK to the <see cref="DeckType"/> (format) this deck box is built for — Commander,
    /// Standard, etc. Only meaningful for <see cref="ContainerType.DeckBox"/>. Null when unset or when
    /// the referenced deck type was deleted (FK is <c>SetNull</c>).</summary>
    public int? DeckTypeId { get; set; }

    /// <summary>The online decklist (Moxfield / Archidekt URL) this deck box is linked to, or null. Only
    /// meaningful for <see cref="ContainerType.DeckBox"/>. Set by hand or automatically when a deck URL
    /// is imported into the box; "Sync from URL" reconciles the box against it.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>When the box was last synced from <see cref="SourceUrl"/> (UTC), or null if never.</summary>
    public DateTime? SourceSyncedAt { get; set; }

    /// <summary>Longest <see cref="SourceUrl"/> accepted.</summary>
    public const int MaxSourceUrlLength = 500;

    /// <summary>When true, this location is grouped under "Always Available" in the collection
    /// overview (alongside the system Bulk location) and is never hidden by the active game filter.
    /// The system Bulk location behaves this way regardless of the stored flag — see
    /// <see cref="IsAlwaysAvailable"/>.</summary>
    public bool AlwaysAvailable { get; set; }

    /// <summary>True if this location should always be shown regardless of game filter: either the
    /// system Bulk location or one the user has explicitly marked <see cref="AlwaysAvailable"/>.
    /// Not persisted (derived) — ignored by EF in <c>OmniCardDbContext.OnModelCreating</c>.</summary>
    public bool IsAlwaysAvailable => IsSystem || AlwaysAvailable;

    public int SlotsPerPage { get; set; } = 9;

    /// <summary>Total logical pages in the binder. A persisted, derived value kept equal to the
    /// sum of <see cref="SheetSides"/> so the web companion and every page-based read keep working
    /// without knowing about sheets. The sheet side-list is the source of truth — see
    /// <see cref="BinderSheetLayout"/>.</summary>
    public int TotalPages { get; set; } = 1;
    public int Columns { get; set; } = 3;

    /// <summary>CSV of each physical sheet's usable side count (1 or 2), in reading order — e.g.
    /// <c>"2,2,1"</c>. Source of truth for the binder's pagination. Null on legacy binders that
    /// predate the sheet model; <see cref="BinderSheetLayout.Parse"/> backfills those from
    /// <see cref="TotalPages"/>.</summary>
    public string? SheetSides { get; set; }

    public ICollection<CollectionCard> Cards { get; set; } = [];
}
