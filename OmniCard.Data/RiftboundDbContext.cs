using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

public class RiftboundDbContext : DbContext
{
    public DbSet<RiftboundCard> Cards => Set<RiftboundCard>();
    public DbSet<HashCorrection> HashCorrections => Set<HashCorrection>();

    public RiftboundDbContext(DbContextOptions<RiftboundDbContext> options) : base(options) { }

    // Bump when the on-disk schema/data source changes incompatibly; a stored
    // user_version below this triggers a wipe-and-redownload in RiftboundService.
    public const int RiftboundSchemaVersion = 1;

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
        // PRAGMA does not accept parameters; value is a compile-time constant.
        cmd.CommandText = $"PRAGMA user_version = {RiftboundSchemaVersion};";
        cmd.ExecuteNonQuery();
    }

    public void ApplySchemaUpgrades()
    {
        var conn = Database.GetDbConnection();
        conn.Open();
        // Reserved for future additive columns (see OptcgDbContext for the pattern).
        AddColumnIfMissing(conn, "EdgeHash INTEGER");
        AddColumnIfMissing(conn, "LocalImagePath TEXT");
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
        var card = modelBuilder.Entity<RiftboundCard>();
        card.HasKey(c => c.Id);
        card.HasIndex(c => c.Name);
        card.HasIndex(c => c.SetId);
        card.HasIndex(c => c.CollectorNumber);
        card.HasIndex(c => c.ImageHash);
        card.HasIndex(c => c.EdgeHash);

        modelBuilder.Entity<HashCorrection>(e =>
        {
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).ValueGeneratedOnAdd();
            e.HasIndex(h => h.ScanHash).IsUnique();
        });
    }
}
