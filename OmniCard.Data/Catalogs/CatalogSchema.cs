using Microsoft.EntityFrameworkCore;

namespace OmniCard.Data.Catalogs;

/// <summary>Additive schema helpers for the per-game catalog DBs. Catalogs use EnsureCreated (not
/// migrations), which never alters an existing database — so a column added to a catalog entity after
/// a DB was first created must be added explicitly, idempotently, at startup.</summary>
public static class CatalogSchema
{
    /// <summary>Adds <paramref name="column"/> to <paramref name="table"/> on SQL Server when it isn't
    /// there yet. <paramref name="definition"/> is the T-SQL type + constraints (a NOT NULL column needs a
    /// DEFAULT so existing rows get a value). No-op for other providers.</summary>
    public static void AddSqlServerColumnIfMissing(DbContext context, string table, string column, string definition)
    {
        if (!context.Database.IsSqlServer())
            return;
        // Identifiers come from code, never user input — interpolation is safe here.
        context.Database.ExecuteSqlRaw(
            $"IF COL_LENGTH('{table}', '{column}') IS NULL ALTER TABLE [{table}] ADD [{column}] {definition};");
    }
}
