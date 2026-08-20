namespace OmniCard.Models;

public class StorageContainer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ContainerType ContainerType { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public int? CoverCardId { get; set; }
    public bool ExcludeFromDeckCheck { get; set; }

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
