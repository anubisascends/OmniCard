using OmniCard.Helpers;
using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Services;

public class PriceRefreshCooldownHelperTests : IDisposable
{
    private readonly string _dir;

    public PriceRefreshCooldownHelperTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"pricecd-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void GetLastRefresh_NoFile_ReturnsNull()
    {
        Assert.Null(PriceRefreshCooldownHelper.GetLastRefresh(_dir, CardGame.Mtg));
    }

    [Fact]
    public void RecordThenIsCooldownActive_IsTrueImmediately()
    {
        PriceRefreshCooldownHelper.RecordRefresh(_dir, CardGame.Mtg);
        Assert.True(PriceRefreshCooldownHelper.IsCooldownActive(_dir, CardGame.Mtg, out var next));
        Assert.True(next > DateTime.UtcNow);
    }

    [Fact]
    public void IsCooldownActive_NoRecord_IsFalse()
    {
        Assert.False(PriceRefreshCooldownHelper.IsCooldownActive(_dir, CardGame.Mtg, out _));
    }

    [Fact]
    public void Record_IsPerGame()
    {
        PriceRefreshCooldownHelper.RecordRefresh(_dir, CardGame.Mtg);
        Assert.True(PriceRefreshCooldownHelper.IsCooldownActive(_dir, CardGame.Mtg, out _));
        Assert.False(PriceRefreshCooldownHelper.IsCooldownActive(_dir, CardGame.OnePiece, out _));
    }

    [Fact]
    public void UsesDedicatedPriceFile_NotBulkDataFile()
    {
        PriceRefreshCooldownHelper.RecordRefresh(_dir, CardGame.Mtg);
        Assert.True(File.Exists(Path.Combine(_dir, "price-refresh-timestamps.json")));
    }
}
