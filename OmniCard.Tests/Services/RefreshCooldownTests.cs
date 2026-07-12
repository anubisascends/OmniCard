using System.Text.Json;
using OmniCard.Models;
using OmniCard.Views.Root;

namespace OmniCard.Tests.Services;

public class RefreshCooldownTests : IDisposable
{
    private readonly string _tempDir;

    public RefreshCooldownTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetLastRefresh_ReturnsNull_WhenNoFile()
    {
        var result = RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.Mtg);
        Assert.Null(result);
    }

    [Fact]
    public void GetLastRefresh_ReturnsTimestamp_WhenFileExists()
    {
        var expected = new DateTime(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc);
        var data = new Dictionary<string, DateTime> { ["Mtg"] = expected };
        File.WriteAllText(
            Path.Combine(_tempDir, "refresh-timestamps.json"),
            JsonSerializer.Serialize(data));

        var result = RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.Mtg);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetLastRefresh_ReturnsNull_ForDifferentGame()
    {
        var data = new Dictionary<string, DateTime> { ["Mtg"] = DateTime.UtcNow };
        File.WriteAllText(
            Path.Combine(_tempDir, "refresh-timestamps.json"),
            JsonSerializer.Serialize(data));

        var result = RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.OnePiece);
        Assert.Null(result);
    }

    [Fact]
    public void RecordRefresh_CreatesFile_WhenNoneExists()
    {
        RefreshCooldownHelper.RecordRefresh(_tempDir, CardGame.OnePiece);

        var result = RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.OnePiece);
        Assert.NotNull(result);
        Assert.True((DateTime.UtcNow - result.Value).TotalSeconds < 5);
    }

    [Fact]
    public void RecordRefresh_PreservesOtherGames()
    {
        var mtgTime = new DateTime(2026, 7, 11, 8, 0, 0, DateTimeKind.Utc);
        var data = new Dictionary<string, DateTime> { ["Mtg"] = mtgTime };
        File.WriteAllText(
            Path.Combine(_tempDir, "refresh-timestamps.json"),
            JsonSerializer.Serialize(data));

        RefreshCooldownHelper.RecordRefresh(_tempDir, CardGame.OnePiece);

        Assert.Equal(mtgTime, RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.Mtg));
        Assert.NotNull(RefreshCooldownHelper.GetLastRefresh(_tempDir, CardGame.OnePiece));
    }

    [Fact]
    public void IsCooldownActive_ReturnsFalse_WhenNoTimestamp()
    {
        Assert.False(RefreshCooldownHelper.IsCooldownActive(_tempDir, CardGame.Mtg, out _));
    }

    [Fact]
    public void IsCooldownActive_ReturnsFalse_WhenOver24Hours()
    {
        var old = DateTime.UtcNow.AddHours(-25);
        var data = new Dictionary<string, DateTime> { ["Mtg"] = old };
        File.WriteAllText(
            Path.Combine(_tempDir, "refresh-timestamps.json"),
            JsonSerializer.Serialize(data));

        Assert.False(RefreshCooldownHelper.IsCooldownActive(_tempDir, CardGame.Mtg, out _));
    }

    [Fact]
    public void IsCooldownActive_ReturnsTrue_WhenUnder24Hours()
    {
        var recent = DateTime.UtcNow.AddHours(-1);
        var data = new Dictionary<string, DateTime> { ["Mtg"] = recent };
        File.WriteAllText(
            Path.Combine(_tempDir, "refresh-timestamps.json"),
            JsonSerializer.Serialize(data));

        Assert.True(RefreshCooldownHelper.IsCooldownActive(_tempDir, CardGame.Mtg, out var nextAvailable));
        Assert.True(nextAvailable > DateTime.UtcNow);
    }
}
