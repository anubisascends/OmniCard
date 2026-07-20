using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

public class OmniCardDbContext : DbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryLot> Lots => Set<InventoryLot>();
    public DbSet<InventoryMovement> Movements => Set<InventoryMovement>();
    public DbSet<StorageContainer> StorageContainers => Set<StorageContainer>();
    public DbSet<EbayListing> EbayListings => Set<EbayListing>();
    public DbSet<MismatchLog> MismatchLogs => Set<MismatchLog>();
    public DbSet<FlagResolution> FlagResolutions => Set<FlagResolution>();
    public DbSet<ScanDiagnosticEvent> ScanDiagnosticEvents => Set<ScanDiagnosticEvent>();
    public DbSet<MigrationState> MigrationState => Set<MigrationState>();

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
            e.HasIndex(p => new { p.Game, p.Category });
            e.HasIndex(p => p.Upc);
            e.HasIndex(p => new { p.Game, p.GameCardId, p.Foil });
        });

        modelBuilder.Entity<InventoryLot>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
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
        });

        modelBuilder.Entity<StorageContainer>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedOnAdd();
            e.Property(s => s.ContainerType).HasConversion<string>();
            e.HasIndex(s => s.Name).IsUnique();

            // CollectionCard is not part of this context's model (it lives in the
            // Phase-1 CollectionDbContext shim); ignore the nav so EF doesn't try to
            // pull that unmapped type into this model.
            e.Ignore(s => s.Cards);
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

        modelBuilder.Entity<MigrationState>(e =>
        {
            e.HasKey(m => m.Key);
        });
    }
}
