using Microsoft.EntityFrameworkCore;
using OmniCard.Data;

namespace OmniCard.Web.Data;

/// <summary>
/// Central place for the SQL Server connection used by the web app's unified store
/// (<see cref="OmniCardDbContext"/>). The per-game catalog DBs stay on SQLite; only this one moved to
/// SQL Server for multi-user concurrency. Migrations for this context live in the OmniCard.Web
/// assembly (the desktop app keeps using SQLite + EnsureCreated and is unaffected).
/// </summary>
public static class SqlServerDb
{
    /// <summary>Local-dev default when no connection string is configured — the SQL Server Express /
    /// default instance with a Windows-auth trusted connection.</summary>
    public const string DefaultConnectionString =
        "Server=localhost;Database=OmniCard;Trusted_Connection=True;TrustServerCertificate=True;";

    public const string MigrationsAssembly = "OmniCard.Web";

    /// <summary>Resolves the connection string from configuration (<c>ConnectionStrings:OmniCard</c>),
    /// falling back to <see cref="DefaultConnectionString"/> for local development.</summary>
    public static string ConnectionString(IConfiguration config) =>
        config.GetConnectionString("OmniCard") ?? DefaultConnectionString;

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(MigrationsAssembly));

    /// <summary>Connection string for a per-game catalog database (one SQL Server DB per game, e.g.
    /// <c>OmniCard_Scryfall</c>). An explicit <c>ConnectionStrings:OmniCard_&lt;suffix&gt;</c> wins;
    /// otherwise the base <see cref="ConnectionString"/> is reused with the database name swapped.</summary>
    public static string CatalogConnectionString(IConfiguration config, string suffix)
    {
        var explicitConn = config.GetConnectionString($"OmniCard_{suffix}");
        if (!string.IsNullOrWhiteSpace(explicitConn))
            return explicitConn;

        return new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString(config))
        {
            InitialCatalog = $"OmniCard_{suffix}",
        }.ConnectionString;
    }

    /// <summary>Configures a catalog context on SQL Server. Catalog DBs are disposable reference caches
    /// (refresh wipes + reloads), so they use <c>EnsureCreated</c> at startup rather than migrations.</summary>
    public static void ConfigureCatalog(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString);
}
