using Microsoft.Data.Sqlite;

namespace OmniCard.Tests.Data;

public class StorageContainerMigrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public StorageContainerMigrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OmniCardTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "collection.db");
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

    private void CreateCardsTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Cards (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Game TEXT NOT NULL,
                GameCardId TEXT NOT NULL,
                Name TEXT NOT NULL,
                SetName TEXT NOT NULL,
                SetCode TEXT NOT NULL,
                Number TEXT NOT NULL,
                Rarity TEXT NOT NULL,
                ImageUri TEXT,
                ScanImagePath TEXT,
                Condition TEXT NOT NULL DEFAULT 'NM',
                IsFoil INTEGER NOT NULL DEFAULT 0,
                PurchasePrice REAL,
                DateAdded TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();
    }

    private void InsertCard(SqliteConnection conn, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO Cards (Game, GameCardId, Name, SetName, SetCode, Number, Rarity, DateAdded) VALUES ('Mtg', 'test-id', '{name}', 'Test Set', 'TST', '001', 'common', '2026-01-01')";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Migration_CreatesStorageContainersTable()
    {
        using var conn = OpenDb();
        CreateCardsTable(conn);

        App.EnsureStorageContainerSchema(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('StorageContainers')";
        var columnCount = (long)cmd.ExecuteScalar()!;
        Assert.True(columnCount >= 5);
    }

    [Fact]
    public void Migration_SeedsBulkContainer()
    {
        using var conn = OpenDb();
        CreateCardsTable(conn);

        App.EnsureStorageContainerSchema(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Name, ContainerType, IsSystem FROM StorageContainers WHERE IsSystem = 1";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("Bulk", reader.GetString(0));
        Assert.Equal("Bulk", reader.GetString(1));
        Assert.Equal(1, reader.GetInt32(2));
    }

    [Fact]
    public void Migration_AddsColumnsToCards()
    {
        using var conn = OpenDb();
        CreateCardsTable(conn);

        App.EnsureStorageContainerSchema(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'ContainerId'";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);

        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'Page'";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);

        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'Slot'";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);

        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Cards') WHERE name = 'Section'";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void Migration_DefaultsExistingCardsToBulk()
    {
        using var conn = OpenDb();
        CreateCardsTable(conn);
        InsertCard(conn, "Test Card 1");
        InsertCard(conn, "Test Card 2");

        App.EnsureStorageContainerSchema(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ContainerId FROM Cards";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            Assert.False(reader.IsDBNull(0));
            Assert.True(reader.GetInt32(0) > 0);
        }
    }

    [Fact]
    public void Migration_IsIdempotent()
    {
        using var conn = OpenDb();
        CreateCardsTable(conn);
        InsertCard(conn, "Test Card");

        App.EnsureStorageContainerSchema(conn);
        App.EnsureStorageContainerSchema(conn); // Run twice

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM StorageContainers WHERE IsSystem = 1";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }
}
