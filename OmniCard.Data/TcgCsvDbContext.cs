using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

// Shared abstract context for all TCGCSV-backed games. Concrete per-game subclasses
// (PokemonDbContext etc.) exist only to give EF distinct types → distinct .db files.
public abstract class TcgCsvDbContext : DbContext
{
    public DbSet<TcgCsvCard> Cards => Set<TcgCsvCard>();
    public DbSet<HashCorrection> HashCorrections => Set<HashCorrection>();

    protected TcgCsvDbContext(DbContextOptions options) : base(options) { }

    // Bump when the on-disk schema/data source changes incompatibly; a stored user_version
    // below this triggers a wipe-and-redownload in TcgCsvGameService.
    public const int TcgCsvSchemaVersion = 1;

    public int GetSchemaVersion()
    {
        var conn = Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void MarkMigrationComplete()
    {
        var conn = Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {TcgCsvSchemaVersion};";
        cmd.ExecuteNonQuery();
    }

    public void ApplySchemaUpgrades()
    {
        var conn = Database.GetDbConnection();
        conn.Open();
        // Additive columns for forward-compatibility (idempotent; safe on read-only DBs).
        AddColumnIfMissing(conn, "EdgeHash INTEGER");
        AddColumnIfMissing(conn, "LocalImagePath TEXT");
        AddColumnIfMissing(conn, "ExtendedDataJson TEXT");
        AddColumnIfMissing(conn, "MarketPrice TEXT");
        AddColumnIfMissing(conn, "FoilMarketPrice TEXT");
        AddColumnIfMissing(conn, "PriceUpdatedAt TEXT");
    }

    private static void AddColumnIfMissing(System.Data.Common.DbConnection conn, string columnDef)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE Cards ADD COLUMN {columnDef}";
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column name") || ex.Message.Contains("readonly"))
        {
            // Column already exists, or the DB is read-only (Web app hitting a not-yet-migrated DB).
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var card = modelBuilder.Entity<TcgCsvCard>();
        card.HasKey(c => c.ProductId);
        card.Property(c => c.ProductId).ValueGeneratedNever();
        card.HasIndex(c => c.Name);
        card.HasIndex(c => c.SetCode);
        card.HasIndex(c => c.CollectorNumber);
        card.HasIndex(c => c.ImageHash);
        card.HasIndex(c => c.EdgeHash);
        card.Property(c => c.PriceUpdatedAt)
            .HasConversion(
                v => v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        modelBuilder.Entity<HashCorrection>(e =>
        {
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).ValueGeneratedOnAdd();
            e.HasIndex(h => h.ScanHash).IsUnique();
        });
    }
}
