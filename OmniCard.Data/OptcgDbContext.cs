using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Models;

namespace OmniCard.Data;

public class OptcgDbContext : DbContext
{
    public DbSet<OptcgCard> Cards => Set<OptcgCard>();
    public DbSet<HashCorrection> HashCorrections => Set<HashCorrection>();

    public OptcgDbContext(DbContextOptions<OptcgDbContext> options) : base(options) { }

    public void ApplySchemaUpgrades()
    {
        var conn = Database.GetDbConnection();
        conn.Open();

        AddColumnIfMissing(conn, "LocalImagePath TEXT");
        AddColumnIfMissing(conn, "CardNumber TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(conn, "VariantIndex INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(conn, "VariantLabel TEXT");
        AddColumnIfMissing(conn, "Artist TEXT");
    }

    private static void AddColumnIfMissing(System.Data.Common.DbConnection conn, string columnDef)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE Cards ADD COLUMN {columnDef}";
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column name"))
        {
            // Column already exists
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

        // HashCorrection entity
        modelBuilder.Entity<HashCorrection>(e =>
        {
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).ValueGeneratedOnAdd();
            e.HasIndex(h => h.ScanHash).IsUnique();
        });
    }
}
