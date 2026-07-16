using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO;
using OmniCard.Models;

namespace OmniCard.Data;

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

    /// <summary>
    /// Repairs One Piece collection rows whose <c>SetCode</c>/<c>SetName</c> were written from the
    /// legacy OPTCG data source (hyphenated codes like <c>OP-16</c>, or bogus composite codes like
    /// <c>OP14-EB04</c>). The canonical values are recovered by joining each row's <c>GameCardId</c>
    /// to the reference <c>optcg.db</c> (<c>CardSetId</c> → <c>SetId</c>/<c>SetName</c>).
    /// Only rows that differ from the canonical values are updated, so this is idempotent and cheap
    /// to run on every startup. Returns the number of rows repaired.
    /// </summary>
    public static int RepairOptcgSetCodes(
        string dataDirectory,
        IDbContextFactory<CollectionDbContext> collectionDbContextFactory,
        ILogger logger)
    {
        var referencePath = Path.Combine(dataDirectory, "optcg.db");
        if (!File.Exists(referencePath))
        {
            logger.LogDebug("No optcg.db reference found; skipping OPTCG set-code repair");
            return 0;
        }

        try
        {
            using var context = collectionDbContextFactory.CreateDbContext();
            var conn = (SqliteConnection)context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                conn.Open();

            using (var attach = conn.CreateCommand())
            {
                attach.CommandText = "ATTACH DATABASE $ref AS refdb";
                attach.Parameters.AddWithValue("$ref", referencePath);
                attach.ExecuteNonQuery();
            }

            try
            {
                // Game is persisted as a string ("OnePiece") via HasConversion<string>().
                // Update only rows whose canonical SetId/SetName differ from what is stored,
                // matched by the intact GameCardId (= reference CardSetId).
                using var update = conn.CreateCommand();
                update.CommandText = """
                    UPDATE Cards
                    SET SetCode = (SELECT r.SetId   FROM refdb.Cards r WHERE r.CardSetId = Cards.GameCardId),
                        SetName = (SELECT r.SetName FROM refdb.Cards r WHERE r.CardSetId = Cards.GameCardId)
                    WHERE Game = 'OnePiece'
                      AND GameCardId <> ''
                      AND EXISTS (
                          SELECT 1 FROM refdb.Cards r
                          WHERE r.CardSetId = Cards.GameCardId
                            AND (r.SetId <> Cards.SetCode OR r.SetName <> Cards.SetName))
                    """;
                var repaired = update.ExecuteNonQuery();
                if (repaired > 0)
                    logger.LogInformation("Repaired legacy OPTCG set codes on {Count} collection rows", repaired);
                else
                    logger.LogDebug("No OPTCG set codes needed repair");
                return repaired;
            }
            finally
            {
                using var detach = conn.CreateCommand();
                detach.CommandText = "DETACH DATABASE refdb";
                detach.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to repair legacy OPTCG set codes");
            return 0;
        }
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
