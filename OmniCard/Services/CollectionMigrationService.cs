using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Services;

public static class CollectionMigrationService
{
    private const string OldMtgBackup = "collection.db.old-mtg.bak";
    private const string OldOptcgFile = "optcg_collection.db";
    private const string OldOptcgBackup = "optcg_collection.db.bak";

    /// <summary>
    /// Migrates data from the old per-game collection databases into the unified collection DB.
    /// Looks for a backup marker file to determine if the old MTG schema needs migration,
    /// and for optcg_collection.db to migrate One Piece data.
    /// </summary>
    public static void MigrateIfNeeded(
        string dataDirectory,
        IDbContextFactory<CollectionDbContext> collectionDbContextFactory,
        ILogger logger)
    {
        var optcgPath = Path.Combine(dataDirectory, OldOptcgFile);
        var collectionPath = Path.Combine(dataDirectory, "collection.db");
        var oldMtgBackupPath = Path.Combine(dataDirectory, OldMtgBackup);

        // Check if old OPTCG collection exists (primary migration indicator)
        bool hasOldOptcg = File.Exists(optcgPath);

        // Check if old MTG collection data exists: either as a .bak file from a prior rename,
        // or as collection.db itself if it still has the old schema columns
        bool hasOldMtgBak = File.Exists(oldMtgBackupPath);
        bool hasOldMtgInline = !hasOldMtgBak && HasOldMtgSchema(collectionPath, logger);
        bool hasOldMtg = hasOldMtgBak || hasOldMtgInline;

        if (!hasOldOptcg && !hasOldMtg)
        {
            logger.LogDebug("No legacy collection data found, skipping migration");
            return;
        }

        logger.LogInformation("Legacy collection data detected, starting migration");

        var migrated = 0;

        // Migrate old MTG data
        if (hasOldMtg)
        {
            // If the old data is still inline in collection.db (hasn't been backed up yet),
            // rename it to .bak first so we can safely create the new-schema DB.
            // Do this before opening a CollectionDbContext so no EF connection holds a
            // file handle that would prevent the rename on Windows.
            if (hasOldMtgInline)
            {
                SqliteConnection.ClearAllPools();
                File.Move(collectionPath, oldMtgBackupPath, overwrite: true);
                logger.LogInformation("Renamed old MTG collection to {BackupPath}", oldMtgBackupPath);

                // Re-create the new-schema DB
                using var newCtx = collectionDbContextFactory.CreateDbContext();
                newCtx.Database.EnsureCreated();
            }

            using var context = collectionDbContextFactory.CreateDbContext();
            migrated += MigrateOldMtg(oldMtgBackupPath, context, logger);
        }

        // Migrate old OPTCG data
        if (hasOldOptcg)
        {
            using (var optcgContext = collectionDbContextFactory.CreateDbContext())
            {
                migrated += MigrateOldOptcg(optcgPath, optcgContext, logger);
            }

            // Release SQLite connection pool handles so the file can be renamed
            SqliteConnection.ClearAllPools();

            // Rename old OPTCG file
            var backupPath = Path.Combine(dataDirectory, OldOptcgBackup);
            File.Move(optcgPath, backupPath, overwrite: true);
            logger.LogInformation("Renamed {OldPath} to {BackupPath}", optcgPath, backupPath);
        }

        logger.LogInformation("Migration complete: {Count} cards migrated", migrated);
    }

    private static bool HasOldMtgSchema(string dbPath, ILogger logger)
    {
        if (!File.Exists(dbPath))
            return false;

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'ScryfallId'";
            var result = cmd.ExecuteScalar();
            return result is long count && count > 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not check old MTG schema");
            return false;
        }
    }

    private static int MigrateOldMtg(string dbPath, CollectionDbContext context, ILogger logger)
    {
        logger.LogInformation("Migrating old MTG collection from {Path}", dbPath);
        var count = 0;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ScryfallId, Name, SetName, SetCode, Number, Rarity, ImageUri, Condition, IsFoil, PurchasePrice, DateAdded FROM Cards";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            context.Cards.Add(new CollectionCard
            {
                Game = CardGame.Mtg,
                GameCardId = reader.GetString(0),
                Name = reader.GetString(1),
                SetName = reader.GetString(2),
                SetCode = reader.GetString(3),
                Number = reader.GetString(4),
                Rarity = reader.GetString(5),
                ImageUri = reader.IsDBNull(6) ? null : reader.GetString(6),
                Condition = reader.GetString(7),
                IsFoil = reader.GetInt64(8) != 0,
                PurchasePrice = reader.IsDBNull(9) ? null : decimal.TryParse(reader.GetString(9), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : null,
                DateAdded = DateTime.TryParse(reader.GetString(10), out var d) ? d : DateTime.UtcNow,
            });
            count++;
        }

        context.SaveChanges();
        logger.LogInformation("Migrated {Count} MTG cards", count);
        return count;
    }

    private static int MigrateOldOptcg(string dbPath, CollectionDbContext context, ILogger logger)
    {
        logger.LogInformation("Migrating old OPTCG collection from {Path}", dbPath);
        var count = 0;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CardSetId, Name, SetName, SetCode, Number, Rarity, ImageUri, Condition, IsFoil, PurchasePrice, DateAdded FROM Cards";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            context.Cards.Add(new CollectionCard
            {
                Game = CardGame.OnePiece,
                GameCardId = reader.GetString(0),
                Name = reader.GetString(1),
                SetName = reader.GetString(2),
                SetCode = reader.GetString(3),
                Number = reader.GetString(4),
                Rarity = reader.GetString(5),
                ImageUri = reader.IsDBNull(6) ? null : reader.GetString(6),
                Condition = reader.GetString(7),
                IsFoil = reader.GetInt64(8) != 0,
                PurchasePrice = reader.IsDBNull(9) ? null : decimal.TryParse(reader.GetString(9), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : null,
                DateAdded = DateTime.TryParse(reader.GetString(10), out var d) ? d : DateTime.UtcNow,
            });
            count++;
        }

        context.SaveChanges();
        logger.LogInformation("Migrated {Count} OPTCG cards", count);
        return count;
    }
}
