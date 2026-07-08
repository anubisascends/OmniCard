using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

public class ScryfallDbContext : DbContext
{
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<RelatedCard> RelatedCards => Set<RelatedCard>();
    public DbSet<HashCorrection> HashCorrections => Set<HashCorrection>();
    public DbSet<SetSymbolHash> SetSymbolHashes => Set<SetSymbolHash>();

    public ScryfallDbContext(DbContextOptions<ScryfallDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var card = modelBuilder.Entity<Card>();

        // Primary key (not auto-generated — comes from Scryfall)
        card.HasKey(c => c.Id);
        card.Property(c => c.Id).ValueGeneratedNever();

        // Indexes
        card.HasIndex(c => c.Name);
        card.HasIndex(c => c.OracleId);
        card.HasIndex(c => c.SetCode);
        card.HasIndex(c => c.CollectorNumber);
        card.HasIndex(c => c.Rarity);
        card.HasIndex(c => c.ImageHash);
        card.HasIndex(c => new { c.SetCode, c.CollectorNumber, c.Lang });

        // Owned types as JSON columns
        card.OwnsOne(c => c.ImageUris, b => b.ToJson());
        card.OwnsOne(c => c.Prices, b => b.ToJson());
        card.OwnsOne(c => c.Preview, b => b.ToJson());

        // JSON value converters for array/dictionary columns
        card.Property(c => c.MultiverseIds).HasJsonConversion();
        card.Property(c => c.Colors).HasJsonConversion();
        card.Property(c => c.ColorIdentity).HasJsonConversion();
        card.Property(c => c.ColorIndicator).HasJsonConversion();
        card.Property(c => c.Keywords).HasJsonConversion();
        card.Property(c => c.ProducedMana).HasJsonConversion();
        card.Property(c => c.Games).HasJsonConversion();
        card.Property(c => c.Finishes).HasJsonConversion();
        card.Property(c => c.PromoTypes).HasJsonConversion();
        card.Property(c => c.ArtistIds).HasJsonConversion();
        card.Property(c => c.FrameEffects).HasJsonConversion();
        card.Property(c => c.AttractionLights).HasJsonConversion();
        card.Property(c => c.Legalities).HasJsonConversion();
        card.Property(c => c.RelatedUris).HasJsonConversion();
        card.Property(c => c.PurchaseUris).HasJsonConversion();

        // RelatedCard entity
        var relatedCard = modelBuilder.Entity<RelatedCard>();
        relatedCard.HasKey(r => r.Id);
        relatedCard.Property(r => r.Id).ValueGeneratedOnAdd();
        relatedCard.HasOne(r => r.Card)
            .WithMany(c => c.RelatedCards)
            .HasForeignKey(r => r.CardId)
            .OnDelete(DeleteBehavior.Cascade);

        // HashCorrection entity
        modelBuilder.Entity<HashCorrection>(e =>
        {
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).ValueGeneratedOnAdd();
            e.HasIndex(h => h.ScanHash).IsUnique();
        });

        // SetSymbolHash entity
        var symbolHash = modelBuilder.Entity<SetSymbolHash>();
        symbolHash.HasKey(s => s.SetCode);
    }

    /// <summary>
    /// Runs schema migrations that EnsureCreated cannot handle on existing databases:
    /// 1. Drops the unique index on (SetCode, CollectorNumber, Lang) and recreates it as non-unique.
    /// 2. Adds LocalImagePath TEXT column if it doesn't already exist.
    /// 3. Adds ArtHash INTEGER column if it doesn't already exist.
    /// 4. Adds ArtScanHash INTEGER column to HashCorrections if it doesn't already exist.
    /// 5. Creates SetSymbolHashes table if it doesn't exist.
    /// Safe to call multiple times.
    /// </summary>
    public static void RunScryfallMigrations(string dbPath)
    {
        if (!System.IO.File.Exists(dbPath))
            return;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();

        // 1. Drop the (possibly unique) index and recreate as non-unique
        cmd.CommandText = """
            DROP INDEX IF EXISTS "IX_Cards_SetCode_CollectorNumber_Lang";
            CREATE INDEX IF NOT EXISTS "IX_Cards_SetCode_CollectorNumber_Lang"
                ON "Cards" ("SetCode", "CollectorNumber", "Lang");
            """;
        cmd.ExecuteNonQuery();

        // 2. Add LocalImagePath column (SQLite throws if it already exists, so wrap in try/catch)
        try
        {
            cmd.CommandText = "ALTER TABLE Cards ADD COLUMN LocalImagePath TEXT";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
        {
            // Column already exists — nothing to do
        }

        // 3. Add ArtHash column (SQLite throws if it already exists, so wrap in try/catch)
        try
        {
            cmd.CommandText = "ALTER TABLE Cards ADD COLUMN ArtHash INTEGER";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
        {
            // Column already exists — nothing to do
        }

        // 4. Add ArtScanHash column to HashCorrections
        try
        {
            cmd.CommandText = "ALTER TABLE HashCorrections ADD COLUMN ArtScanHash INTEGER";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
        {
            // Column already exists — nothing to do
        }

        // 5. Add identifying columns to HashCorrections for re-linking after data refresh
        foreach (var col in new[] { "CardName", "SetCode", "CollectorNumber" })
        {
            try
            {
                cmd.CommandText = $"ALTER TABLE HashCorrections ADD COLUMN {col} TEXT";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
            {
                // Column already exists — nothing to do
            }
        }

        // 6. Create SetSymbolHashes table if it doesn't exist
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS "SetSymbolHashes" (
                "SetCode" TEXT NOT NULL PRIMARY KEY,
                "ImageHash" INTEGER NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
