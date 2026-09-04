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
return 0;

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
