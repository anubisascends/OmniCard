using OmniCard.Collection.ImportExport;
using OmniCard.Shared.ImportExport;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Settings;

namespace OmniCard.Tests.Services.ImportExport;

public class OrderImportTemplateServiceTests : IDisposable
{
    private readonly string _dir;

    public OrderImportTemplateServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "omnicard-tmpl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private sealed class DataPath(string dir) : IDataPathService
    {
        public string DataDirectory => dir;
        public string ScansDirectory => Path.Combine(dir, "scans");
        public string TempScansDirectory => Path.Combine(dir, "temp_scans");
        public string SymbolsCacheDirectory => Path.Combine(dir, "symbols", "sets");
        public string LogsDirectory => Path.Combine(dir, "logs");
        public string TradesDirectory => Path.Combine(dir, "trades");
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }

    private OrderImportTemplateService Svc() => new(new DataPath(_dir));

    [Fact]
    public void GetAll_AlwaysIncludesBuiltInTcgPlayer()
    {
        var tcg = Assert.Single(Svc().GetAll(), t => t.Id == "tcgplayer");
        Assert.True(tcg.IsBuiltIn);
        Assert.Equal(SalesChannel.TcgPlayer, tcg.Channel);
    }

    [Fact]
    public void Save_NewCustomTemplate_AssignsSlug_AndPersists()
    {
        var svc = Svc();
        var saved = svc.Save(new OrderImportTemplate
        {
            Name = "My eBay Export",
            Channel = SalesChannel.Ebay,
            ColumnMappings = new() { [nameof(OrderImportField.OrderNumber)] = "Ref" },
        });

        Assert.Equal("my-ebay-export", saved.Id);
        Assert.False(saved.IsBuiltIn);

        // A fresh service instance reads it back from disk.
        var reloaded = Svc().Get("my-ebay-export");
        Assert.NotNull(reloaded);
        Assert.Equal("Ref", reloaded!.ColumnMappings[nameof(OrderImportField.OrderNumber)]);
        Assert.Equal(SalesChannel.Ebay, reloaded.Channel);
    }

    [Fact]
    public void Save_ExistingCustomTemplate_UpdatesInPlace()
    {
        var svc = Svc();
        var first = svc.Save(new OrderImportTemplate { Name = "Layout" });
        var updated = svc.Save(new OrderImportTemplate
        {
            Id = first.Id,
            Name = "Layout",
            ColumnMappings = new() { [nameof(OrderImportField.OrderNumber)] = "OrderId" },
        });

        Assert.Equal(first.Id, updated.Id);
        Assert.Single(Svc().GetAll(), t => t.Id == first.Id);
    }

    [Fact]
    public void Save_DuplicateName_GetsUniqueSlug()
    {
        var svc = Svc();
        var a = svc.Save(new OrderImportTemplate { Name = "Export" });
        var b = svc.Save(new OrderImportTemplate { Name = "Export" });
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal("export", a.Id);
        Assert.Equal("export-2", b.Id);
    }

    [Fact]
    public void Save_CannotOverwriteBuiltIn()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Svc().Save(new OrderImportTemplate
        {
            Id = "tcgplayer",
            Name = "Hijack",
        }));
        Assert.Contains("built-in", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Delete_RemovesCustom_ButNotBuiltIn()
    {
        var svc = Svc();
        var saved = svc.Save(new OrderImportTemplate { Name = "Temp" });
        Assert.True(svc.Delete(saved.Id));
        Assert.Null(Svc().Get(saved.Id));
        Assert.False(Svc().Delete("tcgplayer"));
    }
}
