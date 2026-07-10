using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class MismatchLogServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<CollectionDbContext> _factory;

    public MismatchLogServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection).Options;
        _factory = new TestDbContextFactory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MismatchLogService CreateService() => new(_factory);

    private static CardMatch MakeMatch(string id, string name, string setCode,
        string number, double? confidence) => new()
    {
        GameSpecificId = id,
        Name = name,
        SetCode = setCode,
        CollectorNumber = number,
        Confidence = confidence,
        Source = new object(),
    };

    private static ScannedCard MakeScannedCard(ulong hash = 0x1234UL) => new()
    {
        TempImagePath = "/tmp/scan.jpg",
        Hash = hash,
    };

    [Fact]
    public async Task LogMismatch_HighConfidenceDifferentIds_PersistsLog()
    {
        var svc = CreateService();
        var old = MakeMatch("old-id", "Old Card", "SET", "1", 85);
        var corrected = MakeMatch("new-id", "New Card", "SET", "2", null);

        await svc.LogMismatchAsync(old, corrected, MakeScannedCard());

        using var ctx = _factory.CreateDbContext();
        var log = Assert.Single(ctx.MismatchLogs.ToList());
        Assert.Equal("old-id", log.OriginalCardId);
        Assert.Equal("new-id", log.CorrectedCardId);
    }

    [Fact]
    public async Task LogMismatch_LowConfidence_DoesNotLog()
    {
        var svc = CreateService();
        await svc.LogMismatchAsync(
            MakeMatch("a", "A", "S", "1", 79),
            MakeMatch("b", "B", "S", "2", null),
            MakeScannedCard());

        using var ctx = _factory.CreateDbContext();
        Assert.Empty(ctx.MismatchLogs.ToList());
    }

    [Fact]
    public async Task LogMismatch_NullConfidence_DoesNotLog()
    {
        var svc = CreateService();
        await svc.LogMismatchAsync(
            MakeMatch("a", "A", "S", "1", null),
            MakeMatch("b", "B", "S", "2", null),
            MakeScannedCard());

        using var ctx = _factory.CreateDbContext();
        Assert.Empty(ctx.MismatchLogs.ToList());
    }

    [Fact]
    public async Task LogMismatch_SameGameSpecificId_DoesNotLog()
    {
        var svc = CreateService();
        await svc.LogMismatchAsync(
            MakeMatch("same-id", "Card", "S", "1", 90),
            MakeMatch("same-id", "Card", "S", "1", null),
            MakeScannedCard());

        using var ctx = _factory.CreateDbContext();
        Assert.Empty(ctx.MismatchLogs.ToList());
    }

    [Fact]
    public async Task LogMismatch_FieldsPopulatedCorrectly()
    {
        var svc = CreateService();
        var old = MakeMatch("orig-id", "Original", "M10", "42", 95);
        var corrected = MakeMatch("corr-id", "Corrected", "M11", "99", null);
        var scan = new ScannedCard { TempImagePath = "/scans/test.jpg", Hash = 0xABCDUL };

        await svc.LogMismatchAsync(old, corrected, scan);

        using var ctx = _factory.CreateDbContext();
        var log = Assert.Single(ctx.MismatchLogs.ToList());
        Assert.Equal(0xABCDUL, log.ScanHash);
        Assert.Equal("/scans/test.jpg", log.ScanImagePath);
        Assert.Equal("orig-id", log.OriginalCardId);
        Assert.Equal("Original", log.OriginalName);
        Assert.Equal("M10", log.OriginalSetCode);
        Assert.Equal("42", log.OriginalNumber);
        Assert.Equal(95, log.OriginalConfidence);
        Assert.Equal("corr-id", log.CorrectedCardId);
        Assert.Equal("Corrected", log.CorrectedName);
        Assert.Equal("M11", log.CorrectedSetCode);
        Assert.Equal("99", log.CorrectedNumber);
    }

    private class TestDbContextFactory(DbContextOptions<CollectionDbContext> options)
        : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }
}
