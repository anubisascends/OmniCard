using System.IO;
using OmniCard.Collection;
using OmniCard.Data;
using Xunit;

namespace OmniCard.Tests.Services;

public class SalesSettingsServiceTests
{
    [Fact]
    public void ForSaleLocationId_Persists_AcrossInstances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dps = new DataPathServiceStub(dir);
            new SalesSettingsService(dps).SetForSaleLocationId(42);
            Assert.Equal(42, new SalesSettingsService(dps).ForSaleLocationId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private sealed class DataPathServiceStub(string dir) : OmniCard.Interfaces.IDataPathService
    {
        public string DataDirectory => dir;
        public string ScansDirectory => dir;
        public string TempScansDirectory => dir;
        public string SymbolsCacheDirectory => dir;
        public string LogsDirectory => dir;
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
