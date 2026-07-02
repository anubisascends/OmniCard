using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

public class CollectionDbContext : DbContext
{
    public DbSet<CollectionCard> Cards => Set<CollectionCard>();
    public DbSet<StorageContainer> StorageContainers => Set<StorageContainer>();
    public DbSet<MismatchLog> MismatchLogs => Set<MismatchLog>();
    public DbSet<FlagResolution> FlagResolutions => Set<FlagResolution>();

    public CollectionDbContext(DbContextOptions<CollectionDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var card = modelBuilder.Entity<CollectionCard>();

        card.HasKey(c => c.Id);
        card.Property(c => c.Id).ValueGeneratedOnAdd();
        card.Property(c => c.Game).HasConversion<string>();

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
            e.HasIndex(f => f.CollectionCardId);

            e.HasOne(f => f.CollectionCard)
                .WithMany()
                .HasForeignKey(f => f.CollectionCardId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
