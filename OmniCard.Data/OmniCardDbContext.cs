using Microsoft.EntityFrameworkCore;
using OmniCard.Shared.Ebay;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Settings;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Tags;
using OmniCard.Shared.Trades;

namespace OmniCard.Data;

public class OmniCardDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryLot> Lots => Set<InventoryLot>();
    public DbSet<InventoryMovement> Movements => Set<InventoryMovement>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<TradeSession> TradeSessions => Set<TradeSession>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<LotTag> LotTags => Set<LotTag>();
    public DbSet<StorageContainer> StorageContainers => Set<StorageContainer>();
    public DbSet<DeckType> DeckTypes => Set<DeckType>();
    public DbSet<EbayListing> EbayListings => Set<EbayListing>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderEdit> OrderEdits => Set<OrderEdit>();
    public DbSet<MismatchLog> MismatchLogs => Set<MismatchLog>();
    public DbSet<FlagResolution> FlagResolutions => Set<FlagResolution>();
    public DbSet<ScanDiagnosticEvent> ScanDiagnosticEvents => Set<ScanDiagnosticEvent>();
    public DbSet<MigrationState> MigrationState => Set<MigrationState>();
    public DbSet<CardList> CardLists => Set<CardList>();
    public DbSet<CardListItem> CardListItems => Set<CardListItem>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();

    public OmniCardDbContext(DbContextOptions<OmniCardDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).ValueGeneratedOnAdd();
            e.Property(p => p.Game).HasConversion<string>();
            e.Property(p => p.Category).HasConversion<string>();
            e.Ignore(p => p.MarketPrice);
            // Task 1 (Phase 3): persisted eBay-derived sealed price, mapped by convention but
            // named explicitly here for discoverability alongside the Ignore(MarketPrice) above.
            e.Property(p => p.LastMarketPrice);
            e.Property(p => p.PriceUpdatedAt);
            e.HasIndex(p => new { p.Game, p.Category });
            e.HasIndex(p => p.Upc);
            e.HasIndex(p => new { p.Game, p.GameCardId, p.Foil, p.FoilType });
        });

        modelBuilder.Entity<InventoryLot>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            // Force SQLite AUTOINCREMENT on the Lots table's rowid so a deleted lot's id is never
            // reused. Without this, INTEGER PRIMARY KEY defaults to plain rowid reuse semantics:
            // if the deleted lot happened to hold the current max id, the very next inserted lot
            // gets that same id back and can be mis-paired with the deleted lot's still-persisted
            // Sell movements in GetRealized/GetMovements (movements are keyed by LotId and are
            // intentionally kept after a lot is deleted).
            // NOTE (residual risk): this annotation only affects newly-created Lots tables (fresh
            // installs, or a fresh EnsureCreated/migration). An already-existing inventory.db has
            // its Lots table already created without AUTOINCREMENT, and SQLite cannot add
            // AUTOINCREMENT to an existing table without a full table rebuild (create new table,
            // copy rows, drop/rename) — which is risky and out of scope here. Pre-existing
            // databases remain narrowly exposed to id reuse until such a migration is written.
            e.HasAnnotation("Sqlite:Autoincrement", true);
            e.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.ProductId);

            // LocationId is nullable (no location assigned yet); losing the container
            // should not delete the lot, so unset the reference instead.
            e.HasOne<StorageContainer>().WithMany().HasForeignKey(l => l.LocationId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(l => l.LocationId);
            e.Property(l => l.FlagReason).HasConversion<string?>();
        });

        modelBuilder.Entity<InventoryMovement>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).ValueGeneratedOnAdd();
            e.Property(m => m.Type).HasConversion<string>();
            e.HasIndex(m => new { m.ProductId, m.Timestamp });
            e.HasIndex(m => m.OrderLineId);
        });

        modelBuilder.Entity<StorageContainer>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedOnAdd();
            e.Property(s => s.ContainerType).HasConversion<string>();
            e.HasIndex(s => s.Name).IsUnique();

            // Assigned game for deck boxes; nullable enum stored as string (like InventoryLot.FlagReason).
            e.Property(s => s.Game).HasConversion<string?>();

            // Deck boxes may reference a DeckType (format). Deleting the type unsets the reference
            // rather than the box (SetNull), matching how a lot survives losing its container.
            e.HasOne<DeckType>().WithMany().HasForeignKey(s => s.DeckTypeId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => s.DeckTypeId);

            // CollectionCard is not part of this context's model (it lives in the
            // Phase-1 CollectionDbContext shim); ignore the nav so EF doesn't try to
            // pull that unmapped type into this model.
            e.Ignore(s => s.Cards);

            // Derived from IsSystem/AlwaysAvailable — not a stored column.
            e.Ignore(s => s.IsAlwaysAvailable);
        });

        modelBuilder.Entity<DeckType>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).ValueGeneratedOnAdd();
            e.Property(d => d.Game).HasConversion<string>();
            e.Property(d => d.Name).IsRequired();
            // One deck-type name per game; the seed key is globally unique (only set on built-ins).
            e.HasIndex(d => new { d.Game, d.Name }).IsUnique();
            e.HasIndex(d => d.BuiltInKey).IsUnique();
        });

        modelBuilder.Entity<MismatchLog>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<FlagResolution>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).ValueGeneratedOnAdd();
            e.HasIndex(f => f.LotId);

            // A lot can accumulate more than one flag-resolution record over time, so this is a
            // regular (non-unique) FK; deleting the lot removes its flag-resolution history too.
            e.HasOne(f => f.Lot).WithMany()
                .HasForeignKey(f => f.LotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScanDiagnosticEvent>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).ValueGeneratedOnAdd();
            e.HasIndex(d => d.ScanHash);
            e.HasIndex(d => d.SessionId);
            e.HasIndex(d => d.EventType);
        });

        modelBuilder.Entity<EbayListing>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.Property(l => l.Status).HasConversion<string>();
            e.Property(l => l.ListingType).HasConversion<string>();
            e.HasIndex(l => l.LotId).IsUnique();
            e.HasIndex(l => l.Status);
            e.HasIndex(l => l.EbayItemId);

            // A lot can have at most one eBay listing at a time (unique index above);
            // deleting the lot removes its listing too.
            e.HasOne(l => l.Lot).WithMany()
                .HasForeignKey(l => l.LotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Listing>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.Property(l => l.Channel).HasConversion<string>();
            e.Property(l => l.Status).HasConversion<string>();
            e.HasIndex(l => l.LotId);
            e.HasIndex(l => l.Status);
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedOnAdd();
            e.HasIndex(c => c.Name);
        });

        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).ValueGeneratedOnAdd();
            e.Property(o => o.Channel).HasConversion<string>();
            e.Property(o => o.Status).HasConversion<string>();
            e.HasIndex(o => o.CustomerId);
            e.HasIndex(o => o.Status);
        });

        modelBuilder.Entity<OrderLine>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.HasIndex(l => l.OrderId);
        });

        modelBuilder.Entity<OrderEdit>(e =>
        {
            e.HasKey(oe => oe.Id);
            e.Property(oe => oe.Id).ValueGeneratedOnAdd();
            e.HasIndex(oe => oe.OrderId);
        });

        modelBuilder.Entity<CardList>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.Property(l => l.Game).HasConversion<string>();
        });

        modelBuilder.Entity<CardListItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).ValueGeneratedOnAdd();
            e.Property(i => i.Source).HasConversion<string>();
            e.HasIndex(i => i.CardListId);
        });

        modelBuilder.Entity<MigrationState>(e =>
        {
            e.HasKey(m => m.Key);
        });

        // JSON <-> object converters for the permission model. Permissions/overrides are small,
        // read whole, and never queried relationally, so a JSON string column is the simplest fit
        // (matches how the app stores other serialized settings). Value comparers let EF change-track
        // these mutable reference-type properties correctly.
        var stringListConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<List<string>, string>(
            v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>());
        var stringListComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
            v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
            v => v == null ? new List<string>() : v.ToList());
        var overridesConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<PermissionOverrides, string>(
            v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            v => System.Text.Json.JsonSerializer.Deserialize<PermissionOverrides>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new PermissionOverrides());
        var overridesComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<PermissionOverrides>(
            (a, b) => System.Text.Json.JsonSerializer.Serialize(a, (System.Text.Json.JsonSerializerOptions?)null)
                   == System.Text.Json.JsonSerializer.Serialize(b, (System.Text.Json.JsonSerializerOptions?)null),
            v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null).GetHashCode(),
            v => System.Text.Json.JsonSerializer.Deserialize<PermissionOverrides>(
                     System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                     (System.Text.Json.JsonSerializerOptions?)null) ?? new PermissionOverrides());

        modelBuilder.Entity<Role>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).ValueGeneratedOnAdd();
            e.Property(r => r.Name).IsRequired();
            e.HasIndex(r => r.Name).IsUnique();
            e.Property(r => r.Permissions)
                .HasConversion(stringListConverter)
                .Metadata.SetValueComparer(stringListComparer);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).ValueGeneratedOnAdd();
            e.Property(u => u.Username).IsRequired();
            e.Property(u => u.PasswordHash).IsRequired();
            // Usernames are unique (case-insensitive matching is handled in the service; the DB index
            // enforces the hard uniqueness constraint under SQL Server's default case-insensitive collation).
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Overrides)
                .HasConversion(overridesConverter)
                .Metadata.SetValueComparer(overridesComparer);
            // No hard FK to Role: a deleted role simply leaves RoleId dangling (resolver treats an
            // unresolved role as "no baseline"), which keeps role deletion cheap and safe.
            e.HasIndex(u => u.RoleId);
        });

        // Optimistic-concurrency tokens for the networked (multi-user) web deployment, which runs on
        // SQL Server. Added as a SQL Server `rowversion` shadow column so it's invisible to the
        // desktop app, which keeps running on SQLite (single-writer, no token needed) with an
        // unchanged schema. A concurrent web write to a stale row throws DbUpdateConcurrencyException,
        // which the API surfaces as HTTP 409. Applied only to the mutable, contention-prone entities.
        if (Database.IsSqlServer())
        {
            foreach (var clrType in ConcurrencyTrackedEntities)
                modelBuilder.Entity(clrType).Property<byte[]>("RowVersion").IsRowVersion();
        }
    }

    /// <summary>Entity types that get a SQL Server concurrency token (see <see cref="OnModelCreating"/>).</summary>
    internal static readonly Type[] ConcurrencyTrackedEntities =
    [
        typeof(Product),
        typeof(InventoryLot),
        typeof(Listing),
        typeof(StorageContainer),
        typeof(Order),
        typeof(OrderLine),
        typeof(Customer),
    ];
}
