using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Settings;
using OmniCard.Web.Services.ScanBatches;

namespace OmniCard.Tests.Web.ScanBatches;

/// <summary>A clock tests move by hand. Local time is UTC so date-named batches are deterministic.</summary>
internal sealed class FakeClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>In-memory SQLite store + a temp directory holding the data dir and a scan folder root.</summary>
internal sealed class ScanBatchFixture : IDisposable
{
    private readonly SqliteConnection _conn;
    public DbContextOptions<OmniCardDbContext> Options { get; }
    public IDbContextFactory<OmniCardDbContext> Factory { get; }
    public string Root { get; }
    public string DataDir { get; }
    public string ScanRoot { get; }
    public DataPath Paths { get; }
    public ScanBatchStorage Storage { get; }
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    public ScanBatchFixture()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        Options = new DbContextOptionsBuilder<OmniCardDbContext>().UseSqlite(_conn).Options;
        using (var ctx = new OmniCardDbContext(Options)) ctx.Database.EnsureCreated();
        Factory = new Factory_(Options);

        Root = Path.Combine(Path.GetTempPath(), "omnicard-batches-" + Guid.NewGuid().ToString("N"));
        DataDir = Path.Combine(Root, "data");
        ScanRoot = Path.Combine(Root, "scans");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ScanRoot);
        Paths = new DataPath(DataDir);
        Storage = new ScanBatchStorage(Paths);
    }

    public OmniCardDbContext Db() => new(Options);

    public void Dispose()
    {
        _conn.Dispose();
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }

    private sealed class Factory_(DbContextOptions<OmniCardDbContext> options) : IDbContextFactory<OmniCardDbContext>
    {
        public OmniCardDbContext CreateDbContext() => new(options);
    }

    internal sealed class DataPath(string dir) : IDataPathService
    {
        public string DataDirectory => dir;
        public string ScansDirectory => Path.Combine(dir, "scans");
        public string TempScansDirectory => Path.Combine(dir, "temp_scans");
        public string SymbolsCacheDirectory => Path.Combine(dir, "symbols", "sets");
        public string LogsDirectory => Path.Combine(dir, "logs");
        public string TradesDirectory => Path.Combine(dir, "trades");
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
