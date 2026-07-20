using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

public class CollectionDbContext : DbContext
{
    public DbSet<CollectionCard> Cards => Set<CollectionCard>();
    public DbSet<StorageContainer> StorageContainers => Set<StorageContainer>();
    public DbSet<MismatchLog> MismatchLogs => Set<MismatchLog>();
    public DbSet<FlagResolution> FlagResolutions => Set<FlagResolution>();
    public DbSet<ScanDiagnosticEvent> ScanDiagnosticEvents => Set<ScanDiagnosticEvent>();
    public DbSet<EbayListing> EbayListings => Set<EbayListing>();

    public CollectionDbContext(DbContextOptions<CollectionDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var card = modelBuilder.Entity<CollectionCard>();

        card.HasKey(c => c.Id);
        card.Property(c => c.Id).ValueGeneratedOnAdd();
        card.Property(c => c.Game).HasConversion<string>();
        card.Property(c => c.FlagReason).HasConversion<string?>();

        card.HasIndex(c => new { c.Game, c.GameCardId });
        card.HasIndex(c => c.Name);
        card.HasIndex(c => c.SetCode);
        card.HasIndex(c => c.Color);
        card.HasIndex(c => c.CardType);

        modelBuilder.Entity<StorageContainer>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedOnAdd();
            e.Property(s => s.ContainerType).HasConversion<string>();
            e.HasIndex(s => s.Name).IsUnique();
        });

        modelBuilder.Entity<CollectionCard>()
            .HasOne(c => c.Container)
            .WithMany(s => s.Cards)
            .HasForeignKey(c => c.ContainerId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<MismatchLog>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<FlagResolution>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).ValueGeneratedOnAdd();

            // FlagResolution.LotId is a Phase-2a rename of what this (Phase-1) context still
            // physically stores as "CollectionCardId" — this db's FlagResolutions rows predate
            // the unified store and genuinely reference Cards(Id), not an InventoryLot. Map the
            // shared property back onto the old column/relationship so collection.db's on-disk
            // schema doesn't need to change.
            e.Property(f => f.LotId).HasColumnName("CollectionCardId");
            e.HasIndex(f => f.LotId);

            e.HasOne<CollectionCard>()
                .WithMany()
                .HasForeignKey(f => f.LotId)
                .OnDelete(DeleteBehavior.Cascade);

            // InventoryLot is not part of this (Phase-1) context's model; ignore the nav so EF
            // doesn't try to pull that unmapped type — and transitively Product — into this model
            // (same rationale as StorageContainer.Cards being Ignore'd in OmniCardDbContext).
            e.Ignore(f => f.Lot);
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

            // Same rationale as FlagResolution.LotId above: keep the physical column/FK on this
            // legacy context pointed at Cards(Id) under its original name.
            e.Property(l => l.LotId).HasColumnName("CollectionCardId");
            e.HasIndex(l => l.LotId).IsUnique();
            e.HasIndex(l => l.Status);
            e.HasIndex(l => l.EbayItemId);

            e.HasOne<CollectionCard>()
                .WithOne(c => c.EbayListing)
                .HasForeignKey<EbayListing>(l => l.LotId)
                .OnDelete(DeleteBehavior.Cascade);

            // Same rationale as FlagResolution.Lot above.
            e.Ignore(l => l.Lot);
        });
    }
}
