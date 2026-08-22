using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;

namespace OmniCard.Web.Services;

/// <summary>
/// Hands out <b>writable</b> <see cref="OmniCardDbContext"/> instances against the shared
/// <c>inventory.db</c>. The rest of the web app deliberately opens every database
/// <c>Mode=ReadOnly</c> (see <c>Program.cs</c>); this factory is the single, deliberate exception,
/// injected only into the binder-editor's write services so the read-only invariant holds
/// everywhere else.
///
/// The desktop app may be writing the same file concurrently, so the connection is opened with a
/// generous busy timeout and the database is put into WAL journal mode (which allows a reader/writer
/// to coexist and greatly reduces <c>SQLITE_BUSY</c>). WAL is a persistent property of the file, so
/// it is set once at construction; the setting is a no-op if the desktop already enabled it.
/// </summary>
public sealed class WritableOmniCardDbContextFactory : IDbContextFactory<OmniCardDbContext>
{
    private readonly string _connectionString;

    public WritableOmniCardDbContextFactory(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite,
            // Busy timeout (seconds) — wait rather than immediately throwing SQLITE_BUSY when the
            // desktop app holds a write lock.
            DefaultTimeout = 30,
        }.ToString();

        TryEnableWal();
    }

    public OmniCardDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OmniCardDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new OmniCardDbContext(options);
    }

    private void TryEnableWal()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort: if WAL can't be set (e.g. the desktop holds an exclusive lock at this
            // instant), the DefaultTimeout above still protects us from transient SQLITE_BUSY.
        }
    }
}
