namespace OmniCard.Shared.Security;

/// <summary>
/// The fixed, code-defined catalog of granular per-section permissions. This is the single source of
/// truth referenced by server-side enforcement (<c>RequirePermissionAttribute</c>), role seeding, and
/// the Administration UI (exposed to the SPA via <c>GET /api/meta/permissions</c>).
///
/// <para>Keys follow <c>area.action</c> (or <c>area.sub.action</c>). Admin accounts
/// (<see cref="Settings.User.IsAdmin"/>/<see cref="Settings.User.IsSystem"/>) bypass this catalog and
/// hold ALL permissions. User/role management is gated on admin, not on any key here.</para>
///
/// <para>The Components tab (open-source attribution) is intentionally NOT represented here — it is
/// always viewable for every signed-in user.</para>
/// </summary>
public static class Permissions
{
    // Dashboard
    public const string DashboardView = "dashboard.view";

    // Scan
    public const string ScanView = "scan.view";
    public const string ScanCommit = "scan.commit";

    // Collection
    public const string CollectionView = "collection.view";
    public const string CollectionEdit = "collection.edit";
    public const string CollectionDelete = "collection.delete";
    public const string CollectionExport = "collection.export";

    // Locations
    public const string LocationsView = "locations.view";
    public const string LocationsCreate = "locations.create";
    public const string LocationsEdit = "locations.edit";
    public const string LocationsDelete = "locations.delete";

    // Binder
    public const string BinderView = "binder.view";
    public const string BinderEdit = "binder.edit";

    // Sets
    public const string SetsView = "sets.view";
    public const string SetsExport = "sets.export";

    // Inventory
    public const string InventoryView = "inventory.view";
    public const string InventoryCreate = "inventory.create";
    public const string InventoryEdit = "inventory.edit";
    public const string InventoryDelete = "inventory.delete";

    // Lists
    public const string ListsView = "lists.view";
    public const string ListsCreate = "lists.create";
    public const string ListsEdit = "lists.edit";
    public const string ListsDelete = "lists.delete";
    public const string ListsCommit = "lists.commit";

    // Trades
    public const string TradesView = "trades.view";
    public const string TradesCreate = "trades.create";
    public const string TradesFinalize = "trades.finalize";
    public const string TradesCancel = "trades.cancel";

    // Import / Export
    public const string ImportRun = "import.run";
    public const string ExportRun = "export.run";

    // Sales · Orders
    public const string SalesOrdersView = "sales.orders.view";
    public const string SalesOrdersCreate = "sales.orders.create";
    public const string SalesOrdersEdit = "sales.orders.edit";
    public const string SalesOrdersDelete = "sales.orders.delete";
    public const string SalesOrdersImport = "sales.orders.import";

    // Sales · Customers
    public const string SalesCustomersView = "sales.customers.view";
    public const string SalesCustomersCreate = "sales.customers.create";
    public const string SalesCustomersEdit = "sales.customers.edit";
    public const string SalesCustomersDelete = "sales.customers.delete";

    // Sales · Listings
    public const string SalesListingsView = "sales.listings.view";
    public const string SalesListingsCreate = "sales.listings.create";
    public const string SalesListingsEdit = "sales.listings.edit";
    public const string SalesListingsDelete = "sales.listings.delete";
    public const string SalesListingsPick = "sales.listings.pick";

    // Settings
    public const string SettingsView = "settings.view";
    public const string SettingsEdit = "settings.edit";

    // Deck Types
    public const string DeckTypesView = "decktypes.view";
    public const string DeckTypesEdit = "decktypes.edit";

    // Catalog Data
    public const string CatalogView = "catalog.view";
    public const string CatalogRefresh = "catalog.refresh";

    // eBay
    public const string EbayView = "ebay.view";
    public const string EbayManage = "ebay.manage";

    /// <summary>The catalog grouped by feature section, in display order, for the admin checklist UI.</summary>
    public static readonly IReadOnlyList<PermissionGroup> Catalog =
    [
        new("dashboard", "Dashboard",
        [
            new(DashboardView, "view", "View"),
        ]),
        new("scan", "Scan",
        [
            new(ScanView, "view", "View"),
            new(ScanCommit, "commit", "Commit scans"),
        ]),
        new("collection", "Collection",
        [
            new(CollectionView, "view", "View"),
            new(CollectionEdit, "edit", "Edit"),
            new(CollectionDelete, "delete", "Delete"),
            new(CollectionExport, "export", "Export"),
        ]),
        new("locations", "Locations",
        [
            new(LocationsView, "view", "View"),
            new(LocationsCreate, "create", "Create"),
            new(LocationsEdit, "edit", "Edit"),
            new(LocationsDelete, "delete", "Delete"),
        ]),
        new("binder", "Binder",
        [
            new(BinderView, "view", "View"),
            new(BinderEdit, "edit", "Edit"),
        ]),
        new("sets", "Sets",
        [
            new(SetsView, "view", "View"),
            new(SetsExport, "export", "Export want list"),
        ]),
        new("inventory", "Inventory",
        [
            new(InventoryView, "view", "View"),
            new(InventoryCreate, "create", "Create"),
            new(InventoryEdit, "edit", "Edit"),
            new(InventoryDelete, "delete", "Delete"),
        ]),
        new("lists", "Lists",
        [
            new(ListsView, "view", "View"),
            new(ListsCreate, "create", "Create"),
            new(ListsEdit, "edit", "Edit"),
            new(ListsDelete, "delete", "Delete"),
            new(ListsCommit, "commit", "Commit to collection"),
        ]),
        new("trades", "Trades",
        [
            new(TradesView, "view", "View"),
            new(TradesCreate, "create", "Create"),
            new(TradesFinalize, "finalize", "Finalize"),
            new(TradesCancel, "cancel", "Cancel"),
        ]),
        new("import", "Import",
        [
            new(ImportRun, "run", "Run import"),
        ]),
        new("export", "Export",
        [
            new(ExportRun, "run", "Run export"),
        ]),
        new("sales.orders", "Sales · Orders",
        [
            new(SalesOrdersView, "view", "View"),
            new(SalesOrdersCreate, "create", "Create"),
            new(SalesOrdersEdit, "edit", "Edit"),
            new(SalesOrdersDelete, "delete", "Delete"),
            new(SalesOrdersImport, "import", "Import orders"),
        ]),
        new("sales.customers", "Sales · Customers",
        [
            new(SalesCustomersView, "view", "View"),
            new(SalesCustomersCreate, "create", "Create"),
            new(SalesCustomersEdit, "edit", "Edit"),
            new(SalesCustomersDelete, "delete", "Delete"),
        ]),
        new("sales.listings", "Sales · Listings",
        [
            new(SalesListingsView, "view", "View"),
            new(SalesListingsCreate, "create", "Create"),
            new(SalesListingsEdit, "edit", "Edit"),
            new(SalesListingsDelete, "delete", "Delete"),
            new(SalesListingsPick, "pick", "Pick / mark picked"),
        ]),
        new("settings", "Settings",
        [
            new(SettingsView, "view", "View"),
            new(SettingsEdit, "edit", "Edit"),
        ]),
        new("decktypes", "Deck Types",
        [
            new(DeckTypesView, "view", "View"),
            new(DeckTypesEdit, "edit", "Edit"),
        ]),
        new("catalog", "Catalog Data",
        [
            new(CatalogView, "view", "View"),
            new(CatalogRefresh, "refresh", "Refresh"),
        ]),
        new("ebay", "eBay",
        [
            new(EbayView, "view", "View"),
            new(EbayManage, "manage", "Manage / connect"),
        ]),
    ];

    /// <summary>Every valid permission key (order-independent set).</summary>
    public static readonly IReadOnlySet<string> All =
        Catalog.SelectMany(g => g.Permissions).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>All <c>*.view</c> keys — the view-only baseline granted to the seeded "Viewer" role.</summary>
    public static readonly IReadOnlySet<string> ViewOnly =
        Catalog.SelectMany(g => g.Permissions)
               .Where(p => p.Action == "view")
               .Select(p => p.Key)
               .ToHashSet(StringComparer.Ordinal);

    /// <summary>True when <paramref name="key"/> is a known permission in the catalog.</summary>
    public static bool IsValid(string? key) => key is not null && All.Contains(key);
}

/// <summary>A feature-section grouping of permissions for the admin checklist UI.</summary>
public sealed record PermissionGroup(string Key, string Label, IReadOnlyList<PermissionDef> Permissions);

/// <summary>A single permission: its full key, the short action name, and a default English label.</summary>
public sealed record PermissionDef(string Key, string Action, string Label);
