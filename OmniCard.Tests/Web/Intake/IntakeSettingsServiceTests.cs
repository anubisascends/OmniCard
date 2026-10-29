using OmniCard.Shared.Intake;
using OmniCard.Shared.Settings;
using OmniCard.Web.Services.Intake;

namespace OmniCard.Tests.Web.Intake;

/// <summary>intake-settings.json: defaults, the clean-up applied on save/load, and the round trip.</summary>
public class IntakeSettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnicard-intake-" + Guid.NewGuid().ToString("N"));
    private readonly IntakeSettingsService _service;

    public IntakeSettingsServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _service = new IntakeSettingsService(new DataPath(_dir));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Defaults_Are_70_Cash_And_80_Trade()
    {
        var s = _service.Get();

        Assert.Equal(70m, s.CashPercent);
        Assert.Equal(80m, s.TradePercent);
        Assert.Equal(100m, s.ConditionPercents["NM"]);
        Assert.Equal(85m, s.ConditionPercents["lp"]);
        Assert.Null(s.LocationId);
        Assert.True(s.CustomerView.ShowCardPrices);
    }

    [Fact]
    public void Save_Cleans_Values_And_Round_Trips()
    {
        _service.Save(new IntakeSettings
        {
            CashPercent = -5m,
            TradePercent = 500m,
            ConditionPercents = new() { ["LP"] = 150m, ["BOGUS"] = 1m },
            BulkThreshold = -1m,
            BulkMode = IntakeBulkMode.Exclude,
            RoundingStep = 0.3m,
            RoundingScope = IntakeRoundingScope.PerCard,
            LocationId = 0,
            CustomerView = new IntakeCustomerView { ShowCardPrices = false },
        });

        var s = _service.Get();
        Assert.Equal(0m, s.CashPercent);
        Assert.Equal(IntakeSettings.MaxPercent, s.TradePercent);
        Assert.Equal(100m, s.ConditionPercents["LP"]);
        Assert.Equal(100m, s.ConditionPercents["DMG"]);
        Assert.False(s.ConditionPercents.ContainsKey("BOGUS"));
        Assert.Equal(0m, s.BulkThreshold);
        Assert.Equal(IntakeBulkMode.Exclude, s.BulkMode);
        Assert.Equal(0m, s.RoundingStep);
        Assert.Equal(IntakeRoundingScope.PerCard, s.RoundingScope);
        Assert.Null(s.LocationId);
        Assert.False(s.CustomerView.ShowCardPrices);
    }

    [Fact]
    public void Corrupt_File_Falls_Back_To_Defaults()
    {
        File.WriteAllText(Path.Combine(_dir, "intake-settings.json"), "{ not json");
        Assert.Equal(70m, _service.Get().CashPercent);
    }

    private sealed class DataPath(string dir) : IDataPathService
    {
        public string DataDirectory => dir;
        public string ScansDirectory => dir;
        public string TempScansDirectory => dir;
        public string SymbolsCacheDirectory => dir;
        public string LogsDirectory => dir;
        public string TradesDirectory => dir;
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
