using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;
using OmniCard.Interfaces;
using OmniCard.Services;
using OmniCard.Collection;

namespace OmniCard.Tests.Services;

public class SealedProductServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SealedProductDbContext> _options;

    public SealedProductServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<SealedProductDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var ctx = new SealedProductDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private ISealedProductService CreateService() =>
        new SealedProductService(new MockFactory(_options));

    [Fact]
    public void CreateTemplate_RoundTrips()
    {
        var service = CreateService();
        var template = service.CreateTemplate(new SealedProductTemplate
        {
            Name = "Test Box",
            SetCode = "blb",
            Upc = "123",
            ProductType = SealedProductType.BoosterBox,
        });

        var loaded = service.GetTemplates();
        Assert.Single(loaded);
        Assert.Equal("Test Box", loaded[0].Name);
        Assert.Equal("123", loaded[0].Upc);
    }

    [Fact]
    public void FindTemplateByUpc_ReturnsMatch()
    {
        var service = CreateService();
        service.CreateTemplate(new SealedProductTemplate
        {
            Name = "Test",
            Upc = "ABC123",
            ProductType = SealedProductType.BoosterPack,
        });

        Assert.NotNull(service.FindTemplateByUpc("ABC123"));
        Assert.Null(service.FindTemplateByUpc("NOTFOUND"));
    }

    [Fact]
    public void AddInstance_CreatesWithTemplate()
    {
        var service = CreateService();
        var template = service.CreateTemplate(new SealedProductTemplate
        {
            Name = "Pack",
            ProductType = SealedProductType.BoosterPack,
        });

        var instance = service.AddInstance(template.Id, 4.99m);
        Assert.Equal(template.Id, instance.TemplateId);
        Assert.Equal(4.99m, instance.PurchasePrice);

        var all = service.GetInstances();
        Assert.Single(all);
    }

    [Fact]
    public void CrackInstance_CreatesChildrenAndRemovesParent()
    {
        var service = CreateService();

        var packTemplate = service.CreateTemplate(new SealedProductTemplate
        {
            Name = "Booster Pack",
            ProductType = SealedProductType.BoosterPack,
        });

        var boxTemplate = service.CreateTemplate(new SealedProductTemplate
        {
            Name = "Booster Box",
            ProductType = SealedProductType.BoosterBox,
            Contents =
            [
                new SealedProductContents
                {
                    Quantity = 6,
                    ChildProductType = SealedProductType.BoosterPack,
                    ChildTemplateId = packTemplate.Id,
                }
            ]
        });

        var box = service.AddInstance(boxTemplate.Id, 600m);
        var children = service.CrackInstance(box.Id);

        Assert.Equal(6, children.Count);
        Assert.All(children, c =>
        {
            Assert.Equal(packTemplate.Id, c.TemplateId);
            Assert.Equal(100m, c.PurchasePrice);
        });

        // Parent should be gone
        var all = service.GetInstances();
        Assert.Equal(6, all.Count);
        Assert.DoesNotContain(all, i => i.Id == box.Id);
    }

    [Fact]
    public void CrackInstance_MultipleContentLines_SplitsCostEvenly()
    {
        var service = CreateService();

        var bundle = service.CreateTemplate(new SealedProductTemplate
        {
            Name = "Bundle",
            ProductType = SealedProductType.Bundle,
            Contents =
            [
                new SealedProductContents { Quantity = 8, ChildProductType = SealedProductType.BoosterPack },
                new SealedProductContents { Quantity = 1, ChildProductType = SealedProductType.PromoPack },
                new SealedProductContents { Quantity = 3, ChildProductType = SealedProductType.FixedPack },
            ]
        });

        var instance = service.AddInstance(bundle.Id, 48m);
        var children = service.CrackInstance(instance.Id);

        // 8 + 1 + 3 = 12 children, $48 / 12 = $4 each
        Assert.Equal(12, children.Count);
        Assert.All(children, c => Assert.Equal(4m, c.PurchasePrice));
    }

    [Fact]
    public void DeleteTemplate_CascadesInstances()
    {
        var service = CreateService();
        var template = service.CreateTemplate(new SealedProductTemplate
        {
            Name = "Pack",
            ProductType = SealedProductType.BoosterPack,
        });
        service.AddInstance(template.Id, 5m);
        service.AddInstance(template.Id, 5m);

        service.DeleteTemplate(template.Id);

        Assert.Empty(service.GetTemplates());
        Assert.Empty(service.GetInstances());
    }

    [Fact]
    public void CrackInstanceWithTemplates_UsesOverrides()
    {
        var service = CreateService();

        var specificPack = service.CreateTemplate(new SealedProductTemplate
        {
            Name = "Specific Promo Pack",
            ProductType = SealedProductType.PromoPack,
        });

        var bundle = service.CreateTemplate(new SealedProductTemplate
        {
            Name = "Bundle",
            ProductType = SealedProductType.Bundle,
            Contents =
            [
                new SealedProductContents { Quantity = 8, ChildProductType = SealedProductType.BoosterPack },
                new SealedProductContents { Quantity = 1, ChildProductType = SealedProductType.PromoPack },
            ]
        });

        var instance = service.AddInstance(bundle.Id, 45m);

        // Reload to get content IDs
        var templates = service.GetTemplates();
        var bundleTemplate = templates.First(t => t.Name == "Bundle");
        var promoContent = bundleTemplate.Contents.First(c => c.ChildProductType == SealedProductType.PromoPack);

        // Override the promo content line to use the specific template
        var overrides = new Dictionary<int, int> { [promoContent.Id] = specificPack.Id };
        var children = service.CrackInstanceWithTemplates(instance.Id, overrides);

        Assert.Equal(9, children.Count); // 8 + 1
        var promoChild = children.First(c => c.TemplateId == specificPack.Id);
        Assert.Equal(5m, promoChild.PurchasePrice); // 45 / 9 = 5
    }

    [Fact]
    public void CreateTemplateFromArchetype_GeneratesCorrectTemplate()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.PlayBoosterBox, "mh3", "Modern Horizons 3", null);

        Assert.Equal("Modern Horizons 3 Play Booster Box", template.Name);
        Assert.Equal("mh3", template.SetCode);
        Assert.Equal(SealedProductType.PlayBoosterBox, template.ProductType);
        Assert.Null(template.Upc);
        Assert.Single(template.Contents);
        Assert.Equal(36, template.Contents[0].Quantity);
        Assert.Equal(SealedProductType.PlayBoosterPack, template.Contents[0].ChildProductType);
    }

    [Fact]
    public void CreateTemplateFromArchetype_WithUpc_StoresUpc()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.Bundle, "blb", "Bloomburrow", "195166253077");

        Assert.Equal("Bloomburrow Bundle", template.Name);
        Assert.Equal("195166253077", template.Upc);
    }

    [Fact]
    public void CreateTemplateFromArchetype_MultipleContents_AllPersisted()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.PrereleaseKit, "mkm", "Murders at Karlov Manor", null);

        Assert.Equal("Murders at Karlov Manor Prerelease Kit", template.Name);
        Assert.Equal(2, template.Contents.Count);
        Assert.Contains(template.Contents, c => c.Quantity == 6 && c.ChildProductType == SealedProductType.PlayBoosterPack);
        Assert.Contains(template.Contents, c => c.Quantity == 1 && c.ChildProductType == SealedProductType.PromoPack);
    }

    [Fact]
    public void CreateTemplateFromArchetype_NullSetName_UsesGeneric()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.BoosterPack, null, null, null);

        Assert.Equal("Generic Booster Pack", template.Name);
    }

    [Fact]
    public void CreateTemplateFromArchetype_ThenCrack_ProducesCorrectChildren()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.PlayBoosterBox, "mh3", "Modern Horizons 3", null);

        var instance = service.AddInstance(template.Id, 180m);
        var children = service.CrackInstance(instance.Id);

        Assert.Equal(36, children.Count);
        Assert.All(children, c => Assert.Equal(5m, c.PurchasePrice)); // 180 / 36 = 5
    }

    [Fact]
    public void CreateTemplateFromArchetype_PrereleaseKit_CracksIntoMixedTypes()
    {
        var service = CreateService();
        var template = service.CreateTemplateFromArchetype(
            SealedProductType.PrereleaseKit, "mkm", "Murders at Karlov Manor", null);

        var instance = service.AddInstance(template.Id, 35m);
        var children = service.CrackInstance(instance.Id);

        Assert.Equal(7, children.Count); // 6 play boosters + 1 promo
        Assert.All(children, c => Assert.Equal(5m, c.PurchasePrice)); // 35 / 7 = 5
    }

    private class MockFactory(DbContextOptions<SealedProductDbContext> options) : IDbContextFactory<SealedProductDbContext>
    {
        public SealedProductDbContext CreateDbContext() => new(options);
    }
}
