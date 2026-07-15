using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

public class OptcgDbContext : DbContext
{
    public DbSet<OptcgCard> Cards => Set<OptcgCard>();
    public DbSet<HashCorrection> HashCorrections => Set<HashCorrection>();

    public OptcgDbContext(DbContextOptions<OptcgDbContext> options) : base(options) { }

    // Identifies data sourced from api.poneglyph.one. A stored user_version below
    // this value means the DB still holds old-API data and must be wiped.
    public const int PoneglyphSchemaVersion = 1;

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
        cmd.CommandText = $"PRAGMA user_version = {PoneglyphSchemaVersion};";
        cmd.ExecuteNonQuery();
    }

    public void ApplySchemaUpgrades()
    {
        var conn = Database.GetDbConnection();
        conn.Open();

        AddColumnIfMissing(conn, "LocalImagePath TEXT");
        AddColumnIfMissing(conn, "CardNumber TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(conn, "VariantIndex INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "VariantLabel TEXT");
        AddColumnIfMissing(conn, "Artist TEXT");
        AddColumnIfMissing(conn, "EdgeHash INTEGER");
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
            // Column already exists, or the database is read-only (e.g. the Web app's
            // read-only connection hitting a not-yet-migrated DB). Either way, skip
            // the ALTER and let the caller serve whatever schema/data already exists.
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var card = modelBuilder.Entity<OptcgCard>();

        card.HasKey(c => c.CardSetId);

        card.HasIndex(c => c.CardName);
        card.HasIndex(c => c.SetId);
        card.HasIndex(c => c.CardNumber);
        card.HasIndex(c => c.CardColor);
        card.HasIndex(c => c.ImageHash);
        card.HasIndex(c => c.EdgeHash);

        // HashCorrection entity
        modelBuilder.Entity<HashCorrection>(e =>
        {
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).ValueGeneratedOnAdd();
            e.HasIndex(h => h.ScanHash).IsUnique();
        });
    }
}
