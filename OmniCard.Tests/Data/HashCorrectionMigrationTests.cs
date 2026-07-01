using Microsoft.Data.Sqlite;

namespace OmniCard.Tests.Data;

public class HashCorrectionMigrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public HashCorrectionMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private SqliteConnection OpenDb()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    [Fact]
    public void Migration_CreatesHashCorrectionsTable()
    {
        using var conn = OpenDb();
        App.EnsureHashCorrectionsTable(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('HashCorrections')";
        var columnCount = (long)cmd.ExecuteScalar()!;
        Assert.Equal(4, columnCount); // Id, ScanHash, CorrectCardId, CreatedAt
    }

    [Fact]
    public void Migration_CreatesIndex()
    {
        using var conn = OpenDb();
        App.EnsureHashCorrectionsTable(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_HashCorrections_ScanHash'";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Migration_IsIdempotent()
    {
        using var conn = OpenDb();
        App.EnsureHashCorrectionsTable(conn);
        App.EnsureHashCorrectionsTable(conn); // Run twice

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('HashCorrections')";
        Assert.Equal(4L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Migration_SupportsUpsert()
    {
        using var conn = OpenDb();
        App.EnsureHashCorrectionsTable(conn);

        using var cmd = conn.CreateCommand();
        // Insert
        cmd.CommandText = "INSERT OR REPLACE INTO HashCorrections (ScanHash, CorrectCardId, CreatedAt) VALUES (12345, 'card-a', '2026-01-01')";
        cmd.ExecuteNonQuery();

        // Upsert same hash
        cmd.CommandText = "INSERT OR REPLACE INTO HashCorrections (ScanHash, CorrectCardId, CreatedAt) VALUES (12345, 'card-b', '2026-01-02')";
        cmd.ExecuteNonQuery();

        // Should have one row with card-b
        cmd.CommandText = "SELECT CorrectCardId FROM HashCorrections WHERE ScanHash = 12345";
        Assert.Equal("card-b", (string)cmd.ExecuteScalar()!);

        cmd.CommandText = "SELECT COUNT(*) FROM HashCorrections";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }
}
