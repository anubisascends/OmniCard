using System.IO;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class EbaySellingSettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnicard-ebaysel-" + Guid.NewGuid().ToString("N"));

    public EbaySellingSettingsServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeDataPath(string dir) : IDataPathService
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

    private EbaySellingSettingsService Create() => new(new FakeDataPath(_dir));

    [Fact]
    public void Get_ReturnsDefaults_WhenNoFile()
    {
        var s = Create().Get();
        Assert.Equal("omnicard-primary", s.MerchantLocationKey);
        Assert.True(s.FreeShipping);
        Assert.Equal(30, s.ReturnWindowDays);
    }

    [Fact]
    public void SaveThenGet_RoundTrips()
    {
        var svc = Create();
        var s = svc.Get();
        s.AddressLine1 = "1 Main St";
        s.Country = "US";
        s.PostalCode = "97201";
        s.FulfillmentPolicyId = "fp-1";
        svc.Save(s);

        var reloaded = Create().Get();
        Assert.Equal("1 Main St", reloaded.AddressLine1);
        Assert.Equal("US", reloaded.Country);
        Assert.Equal("fp-1", reloaded.FulfillmentPolicyId);
    }

    [Fact]
    public void IsSetupComplete_TrueOnlyWhenLocationAndCorePoliciesPresent()
    {
        var svc = Create();
        Assert.False(svc.IsSetupComplete());

        var s = svc.Get();
        s.LocationProvisioned = true;
        s.FulfillmentPolicyId = "fp-1";
        s.ReturnPolicyId = "rp-1";
        svc.Save(s);
        Assert.True(svc.IsSetupComplete());
    }

    [Fact]
    public void Get_FallsBackToDefaults_WhenFileCorrupt()
    {
        File.WriteAllText(Path.Combine(_dir, "ebay-selling.json"), "{ not json");
        var s = Create().Get();
        Assert.Equal("omnicard-primary", s.MerchantLocationKey);
    }
}
