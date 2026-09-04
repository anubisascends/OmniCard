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
}
