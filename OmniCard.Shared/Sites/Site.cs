namespace OmniCard.Shared.Sites;

/// <summary>
/// A <b>site</b> is a MAJOR physical location — a home, a shop, a storage unit — that holds many
/// child storage locations (binders, boxes, deck boxes; see
/// <see cref="Storage.StorageContainer"/>). It sits one level above locations:
/// <c>Site ▸ Location ▸ Card</c>. A site is <em>not</em> a shelf, a binder or a box; it's the
/// building those things live in.
///
/// <para>Sites exist so several people can share one collection database while each only sees the
/// places they're allowed to: e.g. one partner's home site and the other partner's home site, with
/// the children able to see one but not the other. Visibility is granted per site to users and roles
/// (<see cref="SiteAccessGrant"/>); only administrators create, rename, delete or re-permission sites.</para>
///
/// <para>Every database has exactly one <see cref="IsDefault"/> site (id <see cref="DefaultSiteId"/>,
/// seeded by the <c>AddSites</c> migration). New locations land there unless another site is chosen,
/// pre-sites locations were backfilled into it, the system Bulk location always lives there, and it
/// is always readable and writable by every signed-in user. It can be renamed but never deleted.</para>
/// </summary>
public class Site
{
    /// <summary>Primary key of the seeded default site. Fixed so the migration can backfill every
    /// pre-existing location to it and the storage-container column can default to it.</summary>
    public const int DefaultSiteId = 1;

    /// <summary>Name the default site is seeded with (admins may rename it).</summary>
    public const string DefaultSiteName = "Default";

    public int Id { get; set; }

    /// <summary>Display name, unique (case-insensitive) across sites — e.g. "Andrew's House".</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional free-text note, e.g. the address or who lives there.</summary>
    public string? Description { get; set; }

    /// <summary>True only for the single seeded default site: visible to everyone, not deletable.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Display order after the default site (which always sorts first).</summary>
    public int SortOrder { get; set; }
}

/// <summary>How much a user (or role) may do with the locations and cards in a site. Ordered so the
/// effective level is simply the maximum of everything granted.</summary>
public enum SiteAccessLevel
{
    /// <summary>The site and everything in it is hidden.</summary>
    None = 0,

    /// <summary>See the site's locations and cards (browse, search, add owned cards to lists).</summary>
    Read = 1,

    /// <summary>Read plus change the site's locations and the cards in them (still subject to the
    /// normal section permissions such as <c>collection.edit</c>).</summary>
    Write = 2,
}

/// <summary>Who a <see cref="SiteAccessGrant"/> applies to.</summary>
public enum SitePrincipalType
{
    User = 0,
    Role = 1,
}

/// <summary>
/// One access grant on a site: "<see cref="PrincipalType"/> #<see cref="PrincipalId"/> may
/// <see cref="Level"/> site #<see cref="SiteId"/>". A user's effective level on a site is the highest
/// of their own grant and their role's grant; with no grant the site is hidden. Administrators see and
/// edit every site regardless, and the default site needs no grant.
/// </summary>
public class SiteAccessGrant
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public SitePrincipalType PrincipalType { get; set; }

    /// <summary><see cref="Settings.User.Id"/> or <see cref="Settings.Role.Id"/> depending on
    /// <see cref="PrincipalType"/>. No hard FK (a principal can be either table); grants are removed
    /// when the user/role is deleted.</summary>
    public int PrincipalId { get; set; }

    public SiteAccessLevel Level { get; set; }
}
