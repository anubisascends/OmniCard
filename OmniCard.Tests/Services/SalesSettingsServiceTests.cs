using System.IO;
using System.Linq;
using OmniCard.Collection;
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

    [Fact]
    public void OrdersEditorWidthAndCollapsed_Persist_AcrossInstances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dps = new DataPathServiceStub(dir);
            var svc = new SalesSettingsService(dps);
            svc.SetOrdersEditorWidth(432);
            svc.SetOrdersEditorCollapsed(true);

            var reloaded = new SalesSettingsService(dps);
            Assert.Equal(432, reloaded.OrdersEditorWidth);
            Assert.True(reloaded.OrdersEditorCollapsed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void OrdersEditor_Defaults_WhenUnset()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var svc = new SalesSettingsService(new DataPathServiceStub(dir));
            Assert.Null(svc.OrdersEditorWidth);
            Assert.False(svc.OrdersEditorCollapsed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ForSaleLocationId_Returns_Null_OnCorruptJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, "sales-settings.json");
            File.WriteAllText(filePath, "{ not valid json");
            var dps = new DataPathServiceStub(dir);
            Assert.Null(new SalesSettingsService(dps).ForSaleLocationId);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Company_And_Receipt_Persist_AcrossInstances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dps = new DataPathServiceStub(dir);
            var svc = new SalesSettingsService(dps);
            svc.SaveCompany(new OmniCard.Models.CompanyProfile { Name = "Acme Cards", City = "Reno", State = "NV" });
            svc.SaveReceipt(new OmniCard.Models.ReceiptSettings { WidthMm = 58, ShowPrices = false, FooterText = "Thanks!" });

            var reloaded = new SalesSettingsService(dps);
            Assert.Equal("Acme Cards", reloaded.GetCompany().Name);
            Assert.Equal("Reno", reloaded.GetCompany().City);
            Assert.Equal(58, reloaded.GetReceipt().WidthMm);
            Assert.False(reloaded.GetReceipt().ShowPrices);
            Assert.Equal("Thanks!", reloaded.GetReceipt().FooterText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GetCompany_And_GetReceipt_ReturnDefaults_ForOldFileWithoutThem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // A pre-phase-3 file: only ForSaleLocationId present.
            File.WriteAllText(Path.Combine(dir, "sales-settings.json"), "{\"ForSaleLocationId\":7}");
            var svc = new SalesSettingsService(new DataPathServiceStub(dir));

            Assert.Equal(7, svc.ForSaleLocationId);
            Assert.NotNull(svc.GetCompany());
            Assert.Null(svc.GetCompany().Name);
            Assert.Equal(80, svc.GetReceipt().WidthMm);   // default
            Assert.True(svc.GetReceipt().ShowPrices);      // default
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void SetLogo_CopiesFileIntoDataDir_ReturnsRelativeName()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        var srcDir = Path.Combine(Path.GetTempPath(), "omnicard-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(srcDir);
        try
        {
            var src = Path.Combine(srcDir, "mylogo.png");
            File.WriteAllBytes(src, new byte[] { 1, 2, 3, 4 });
            var svc = new SalesSettingsService(new DataPathServiceStub(dir));

            var rel = svc.SetLogo(src);

            Assert.False(Path.IsPathRooted(rel));                       // relative
            Assert.True(File.Exists(Path.Combine(dir, rel)));           // resolvable against data dir
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(dir, rel)));
        }
        finally { Directory.Delete(dir, recursive: true); Directory.Delete(srcDir, recursive: true); }
    }

    [Fact]
    public void GetWorkflowLanes_ReturnsDefaults_WhenNoneSaved()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var svc = new SalesSettingsService(new DataPathServiceStub(dir));
            var lanes = svc.GetWorkflowLanes();
            Assert.Equal(OmniCard.Models.WorkflowLane.Defaults().Count, lanes.Count);
            Assert.Equal("created", lanes[0].Key);
            Assert.Contains(lanes, l => l.Behavior == OmniCard.Models.OrderStatus.Shipped);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void WorkflowLanes_Persist_AcrossInstances_PreservingOrder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-sales-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dps = new DataPathServiceStub(dir);
            new SalesSettingsService(dps).SaveWorkflowLanes(
            [
                new() { Key = "new", Name = "New", Color = "#111111", Behavior = OmniCard.Models.OrderStatus.Created },
                new() { Key = "await", Name = "Awaiting Payment", Color = "#222222", Behavior = OmniCard.Models.OrderStatus.Created },
                new() { Key = "ship", Name = "Out the Door", Color = "#333333", Behavior = OmniCard.Models.OrderStatus.Shipped },
            ]);

            var reloaded = new SalesSettingsService(dps).GetWorkflowLanes();
            Assert.Equal(3, reloaded.Count);
            Assert.Equal(["new", "await", "ship"], reloaded.Select(l => l.Key));
            Assert.Equal("Awaiting Payment", reloaded[1].Name);
            Assert.Equal(OmniCard.Models.OrderStatus.Shipped, reloaded[2].Behavior);
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
        public string TradesDirectory => dir;
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
