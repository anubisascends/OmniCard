using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Interfaces;
using OmniCard.Services;

namespace OmniCard.Tests.Services;

public class ScanDiagnosticServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<CollectionDbContext> _options;

    public ScanDiagnosticServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<CollectionDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new CollectionDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private IScanDiagnosticService CreateService() =>
        new ScanDiagnosticService(new MockFactory(_options));

    [Fact]
    public void LogScanCompleted_CreatesEvent()
    {
        var service = CreateService();
        var match = new CardMatch
        {
            Name = "Lightning Bolt",
            SetCode = "m21",
            CollectorNumber = "199",
            GameSpecificId = "abc-123",
            Confidence = 87.5,
        };
        var diagnostics = new MatchDiagnostics
        {
            DecisionPhase = "PHashConfident",
            PHashDistance = 3,
            TieZoneCandidates =
            [
                new TieZoneCandidate { CardId = "abc-123", Name = "Lightning Bolt", SetCode = "m21", CollectorNumber = "199", PHashDistance = 3, FinalScore = -2, Selected = true },
                new TieZoneCandidate { CardId = "def-456", Name = "Lightning Bolt", SetCode = "2xm", CollectorNumber = "141", PHashDistance = 4, FinalScore = 4, Selected = false },
            ],
        };

        service.LogScanCompleted("session-1", 0xA3F7B2C1, match, diagnostics, [12345UL, 67890UL], null, FlagReason.None);

        Assert.Equal(1, service.GetEventCount());

        using var ctx = new CollectionDbContext(_options);
        var evt = ctx.ScanDiagnosticEvents.Single();
        Assert.Equal("ScanCompleted", evt.EventType);
        Assert.Equal("session-1", evt.SessionId);
        Assert.Equal(0xA3F7B2C1UL, evt.ScanHash);

        var payload = JsonDocument.Parse(evt.Payload);
        Assert.Equal("Lightning Bolt", payload.RootElement.GetProperty("matchedName").GetString());
        Assert.Equal("PHashConfident", payload.RootElement.GetProperty("decisionPhase").GetString());
        Assert.Equal(2, payload.RootElement.GetProperty("tieZoneCandidates").GetArrayLength());
    }

    [Fact]
    public void LogUserCorrected_SetsWasInTieZone()
    {
        var service = CreateService();

        // First log a scan with a tie zone
        var match = new CardMatch { Name = "Bolt", SetCode = "m21", CollectorNumber = "199", GameSpecificId = "abc-123", Confidence = 80 };
        var diagnostics = new MatchDiagnostics
        {
            DecisionPhase = "PHashConfident",
            PHashDistance = 3,
            TieZoneCandidates =
            [
                new TieZoneCandidate { CardId = "abc-123", Name = "Bolt", SetCode = "m21", Selected = true },
                new TieZoneCandidate { CardId = "def-456", Name = "Bolt", SetCode = "2xm", Selected = false },
            ],
        };
        service.LogScanCompleted("s1", 0xAAAA, match, diagnostics, null, null, FlagReason.None);

        // Now correct to "def-456" which was in the tie zone
        var card = new ScannedCard { TempImagePath = "", Hash = 0xAAAA, Match = match, FlagReason = FlagReason.Manual };
        var newMatch = new CardMatch { Name = "Bolt", SetCode = "2xm", CollectorNumber = "141", GameSpecificId = "def-456", Confidence = 100 };
        service.LogUserCorrected(0xAAAA, card, newMatch);

        using var ctx = new CollectionDbContext(_options);
        var evt = ctx.ScanDiagnosticEvents.Where(e => e.EventType == "UserCorrected").Single();
        var payload = JsonDocument.Parse(evt.Payload);
        Assert.True(payload.RootElement.GetProperty("wasInTieZone").GetBoolean());
    }

    [Fact]
    public void LogUserCorrected_WasInTieZone_FalseWhenNotInTieZone()
    {
        var service = CreateService();

        var match = new CardMatch { Name = "Bolt", SetCode = "m21", GameSpecificId = "abc-123", Confidence = 80 };
        var diagnostics = new MatchDiagnostics
        {
            DecisionPhase = "PHashConfident",
            PHashDistance = 3,
            TieZoneCandidates = [new TieZoneCandidate { CardId = "abc-123", Selected = true }],
        };
        service.LogScanCompleted("s1", 0xBBBB, match, diagnostics, null, null, FlagReason.None);

        var card = new ScannedCard { TempImagePath = "", Hash = 0xBBBB, Match = match, FlagReason = FlagReason.Manual };
        var newMatch = new CardMatch { Name = "Other Card", SetCode = "leg", GameSpecificId = "xyz-999", Confidence = 100 };
        service.LogUserCorrected(0xBBBB, card, newMatch);

        using var ctx = new CollectionDbContext(_options);
        var evt = ctx.ScanDiagnosticEvents.Where(e => e.EventType == "UserCorrected").Single();
        var payload = JsonDocument.Parse(evt.Payload);
        Assert.False(payload.RootElement.GetProperty("wasInTieZone").GetBoolean());
    }

    [Fact]
    public void ClearDiagnostics_RemovesAllEvents()
    {
        var service = CreateService();
        service.LogScanCompleted("s1", 0x1111, null, null, null, null, FlagReason.NoMatch);
        service.LogScanCompleted("s1", 0x2222, null, null, null, null, FlagReason.NoMatch);
        Assert.Equal(2, service.GetEventCount());

        service.ClearDiagnostics();
        Assert.Equal(0, service.GetEventCount());
    }

    [Fact]
    public void LogUserFlagged_CreatesEvent()
    {
        var service = CreateService();
        var card = new ScannedCard
        {
            TempImagePath = "", Hash = 0xCCCC,
            Match = new CardMatch { Name = "Bolt", SetCode = "m21", GameSpecificId = "abc", Confidence = 85 },
        };
        service.LogScanCompleted("s1", 0xCCCC, card.Match, null, null, null, FlagReason.None);
        service.LogUserFlagged(0xCCCC, card);

        using var ctx = new CollectionDbContext(_options);
        var evt = ctx.ScanDiagnosticEvents.Where(e => e.EventType == "UserFlagged").Single();
        var payload = JsonDocument.Parse(evt.Payload);
        Assert.Equal("Manual", payload.RootElement.GetProperty("flagReason").GetString());
    }

    [Fact]
    public void GetEventCount_ReturnsCorrectCount()
    {
        var service = CreateService();
        Assert.Equal(0, service.GetEventCount());

        service.LogScanCompleted("s1", 0x1111, null, null, null, null, FlagReason.NoMatch);
        Assert.Equal(1, service.GetEventCount());
    }

    private class MockFactory(DbContextOptions<CollectionDbContext> options) : IDbContextFactory<CollectionDbContext>
    {
        public CollectionDbContext CreateDbContext() => new(options);
    }
}
