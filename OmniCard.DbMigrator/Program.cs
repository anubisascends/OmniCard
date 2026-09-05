using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.DbMigrator;

// One-time copier: SQLite inventory.db (desktop's unified store) -> SQL Server (the web app's
// multi-user store). Preserves primary-key ids (so every FK stays valid) via SET IDENTITY_INSERT,
// and disables FK constraint checking during the load so table order doesn't matter. Idempotent:
// it clears the target tables first, so it can be re-run.
//
// Usage: dotnet run --project OmniCard.DbMigrator -- "<dataDir>" ["<sqlserver-connstring>"]

var dataDir = args.Length > 0 ? args[0] : @"X:\TCG Card Scanner";
var targetConn = args.Length > 1
    ? args[1]
    : "Server=localhost;Database=OmniCard;Trusted_Connection=True;TrustServerCertificate=True;";
var sqlitePath = Path.Combine(dataDir, "inventory.db");

if (!File.Exists(sqlitePath))
{
    Console.Error.WriteLine($"Source not found: {sqlitePath}");
    return 1;
}

// Bring the source SQLite schema up to the current model (same patch the desktop/web startup runs),
// so EF can read every mapped column.
UnifiedMigrationService.EnsureUnifiedSchema(dataDir, NullLogger.Instance);

var srcOptions = new DbContextOptionsBuilder<OmniCardDbContext>()
    .UseSqlite($"Data Source={sqlitePath};Mode=ReadOnly").Options;
var dstOptions = new DbContextOptionsBuilder<OmniCardDbContext>()
    .UseSqlServer(targetConn).Options;

using var src = new OmniCardDbContext(srcOptions);
using var dst = new MigrationTargetContext(dstOptions);
dst.ChangeTracker.AutoDetectChangesEnabled = false;

var entityTypes = dst.Model.GetEntityTypes()
    .Where(e => e.BaseType == null && e.FindPrimaryKey() != null && e.GetTableName() != null)
    .Select(e => e.ClrType)
    .Distinct()
    .ToList();
var tables = entityTypes
    .Select(t => dst.Model.FindEntityType(t)!.GetTableName()!)
    .Distinct()
    .ToList();

Console.WriteLine($"Copying {sqlitePath}");
Console.WriteLine($"     -> {targetConn}");

// Clear + unlock the target so the copy is a clean, re-runnable load.
foreach (var table in tables)
    dst.Database.ExecuteSqlRaw($"ALTER TABLE [{table}] NOCHECK CONSTRAINT ALL");
foreach (var table in tables)
    dst.Database.ExecuteSqlRaw($"DELETE FROM [{table}]");

var total = 0;
foreach (var clr in entityTypes)
{
    var et = dst.Model.FindEntityType(clr)!;
    var table = et.GetTableName()!;
    var key = et.FindPrimaryKey();
    var identity = key is { Properties.Count: 1 }
                   && (key.Properties[0].ClrType == typeof(int) || key.Properties[0].ClrType == typeof(long));

    var rows = ReadAll(src, clr);
    if (rows.Count == 0)
    {
        Console.WriteLine($"  {table}: 0");
        continue;
    }

    using var tx = dst.Database.BeginTransaction();
    if (identity) dst.Database.ExecuteSqlRaw($"SET IDENTITY_INSERT [{table}] ON");
    dst.AddRange(rows);
    dst.SaveChanges();
    if (identity) dst.Database.ExecuteSqlRaw($"SET IDENTITY_INSERT [{table}] OFF");
    tx.Commit();
    dst.ChangeTracker.Clear();

    total += rows.Count;
    Console.WriteLine($"  {table}: {rows.Count}");
}

// Re-check the FK constraints now that all rows are present.
foreach (var table in tables)
    dst.Database.ExecuteSqlRaw($"ALTER TABLE [{table}] WITH CHECK CHECK CONSTRAINT ALL");

Console.WriteLine($"Done. {total} rows copied across {tables.Count} tables.");

// --- Per-game catalog DBs: SQLite files -> one SQL Server DB per game (OmniCard_<Game>) ---
Console.WriteLine();
Console.WriteLine("Copying per-game catalog databases...");
var catalogTotal = 0;
catalogTotal += CopyCatalog<ScryfallDbContext>(Path.Combine(dataDir, "scryfall.db"), CatalogConn(targetConn, "Scryfall"));
catalogTotal += CopyCatalog<OptcgDbContext>(Path.Combine(dataDir, "optcg.db"), CatalogConn(targetConn, "Optcg"));
catalogTotal += CopyCatalog<RiftboundDbContext>(Path.Combine(dataDir, "riftbound.db"), CatalogConn(targetConn, "Riftbound"));
catalogTotal += CopyCatalog<PokemonDbContext>(Path.Combine(dataDir, "pokemon.db"), CatalogConn(targetConn, "Pokemon"));
catalogTotal += CopyCatalog<YugiohDbContext>(Path.Combine(dataDir, "yugioh.db"), CatalogConn(targetConn, "Yugioh"));
catalogTotal += CopyCatalog<FinalFantasyDbContext>(Path.Combine(dataDir, "fftcg.db"), CatalogConn(targetConn, "FinalFantasy"));
Console.WriteLine($"Catalog copy done. {catalogTotal} rows across all games.");
return 0;

// Base OmniCard connection string with the database name swapped to the per-game catalog DB.
static string CatalogConn(string baseConn, string suffix) =>
    new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(baseConn) { InitialCatalog = $"OmniCard_{suffix}" }.ConnectionString;

// Generic SQLite->SQL Server copy for a catalog context. EnsureCreated builds the target DB + schema
// (catalogs are disposable caches, no migrations); the copy preserves keys, using IDENTITY_INSERT only
// for store-generated (identity) keys and inserting explicit values for ValueGeneratedNever keys.
static int CopyCatalog<TContext>(string sqlitePath, string targetConn) where TContext : DbContext
{
    if (!File.Exists(sqlitePath))
    {
        Console.WriteLine($"  (skip, no source file: {Path.GetFileName(sqlitePath)})");
        return 0;
    }

    var srcOpts = new DbContextOptionsBuilder<TContext>().UseSqlite($"Data Source={sqlitePath};Mode=ReadOnly").Options;
    var dstOpts = new DbContextOptionsBuilder<TContext>().UseSqlServer(targetConn).Options;
    using var src = (TContext)Activator.CreateInstance(typeof(TContext), srcOpts)!;
    using var dst = (TContext)Activator.CreateInstance(typeof(TContext), dstOpts)!;
    // No-tracking source reads so streaming a large catalog doesn't accumulate tracked entities.
    src.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    dst.ChangeTracker.AutoDetectChangesEnabled = false;
    dst.Database.EnsureCreated();

    var entityTypes = dst.Model.GetEntityTypes()
        // Exclude owned types (ImageUris/Prices/Preview): they're stored as JSON inside the owning
        // Card row and are copied with it — they can't be Set<>'d or queried on their own.
        .Where(e => e.BaseType == null && !e.IsOwned() && e.FindPrimaryKey() != null && e.GetTableName() != null)
        .Select(e => e.ClrType).Distinct().ToList();
    var tables = entityTypes.Select(t => dst.Model.FindEntityType(t)!.GetTableName()!).Distinct().ToList();

    Console.WriteLine($"  {Path.GetFileName(sqlitePath)} -> {new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(targetConn).InitialCatalog}");
    foreach (var table in tables) dst.Database.ExecuteSqlRaw($"ALTER TABLE [{table}] NOCHECK CONSTRAINT ALL");
    foreach (var table in tables) dst.Database.ExecuteSqlRaw($"DELETE FROM [{table}]");

    var total = 0;
    const int BatchSize = 2000;
    foreach (var clr in entityTypes)
    {
        var et = dst.Model.FindEntityType(clr)!;
        var table = et.GetTableName()!;
        var key = et.FindPrimaryKey();
        var identity = key is { Properties.Count: 1 }
                       && key.Properties[0].ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd
                       && (key.Properties[0].ClrType == typeof(int) || key.Properties[0].ClrType == typeof(long));

        // Stream the source rows and insert in bounded batches so a large catalog (Scryfall has
        // 100k+ cards with big JSON blobs) doesn't materialize entirely in memory. One transaction
        // per batch, clearing the change tracker each time to keep tracked-entity count flat.
        var count = 0;
        var batch = new List<object>(BatchSize);
        void Flush()
        {
            if (batch.Count == 0) return;
            using var tx = dst.Database.BeginTransaction();
            if (identity) dst.Database.ExecuteSqlRaw($"SET IDENTITY_INSERT [{table}] ON");
            dst.AddRange(batch);
            dst.SaveChanges();
            if (identity) dst.Database.ExecuteSqlRaw($"SET IDENTITY_INSERT [{table}] OFF");
            tx.Commit();
            dst.ChangeTracker.Clear();
            count += batch.Count;
            batch.Clear();
        }

        foreach (var row in StreamAll(src, clr))
        {
            batch.Add(row);
            if (batch.Count >= BatchSize)
                Flush();
        }
        Flush();

        total += count;
        Console.WriteLine($"    {table}: {count}");
    }

    foreach (var table in tables) dst.Database.ExecuteSqlRaw($"ALTER TABLE [{table}] WITH CHECK CHECK CONSTRAINT ALL");
    return total;
}

// Streams rows from a DbSet without buffering them all into a list (paired with no-tracking on the
// source context) so a large catalog can be copied in bounded memory.
static IEnumerable<object> StreamAll(DbContext ctx, Type clr)
{
    var setMethod = typeof(DbContext).GetMethods()
        .Single(m => m.Name == nameof(DbContext.Set)
                     && m.IsGenericMethodDefinition
                     && m.GetParameters().Length == 0)
        .MakeGenericMethod(clr);
    var query = (IQueryable)setMethod.Invoke(ctx, null)!;
    foreach (var o in query)
        yield return o;
}

static List<object> ReadAll(DbContext ctx, Type clr)
{
    var setMethod = typeof(DbContext).GetMethods()
        .Single(m => m.Name == nameof(DbContext.Set)
                     && m.IsGenericMethodDefinition
                     && m.GetParameters().Length == 0)
        .MakeGenericMethod(clr);
    var query = (IQueryable)setMethod.Invoke(ctx, null)!;
    var list = new List<object>();
    foreach (var o in (IEnumerable)query)
        list.Add(o);
    return list;
}
