using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Data;

public class SealedProductDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SealedProductDbContext> _options;

    public SealedProductDbContextTests()
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

    [Fact]
    public void CanCreateTemplateWithContents()
    {
        using var ctx = new SealedProductDbContext(_options);

        var boosterTemplate = new SealedProductTemplate
        {
            Name = "Bloomburrow Booster Pack",
            SetCode = "blb",
            ProductType = SealedProductType.BoosterPack,
        };
        ctx.Templates.Add(boosterTemplate);
        ctx.SaveChanges();

        var boxTemplate = new SealedProductTemplate
        {
            Name = "Bloomburrow Booster Box",
            SetCode = "blb",
            Upc = "195166253060",
            ProductType = SealedProductType.BoosterBox,
            Contents =
            [
                new SealedProductContents
                {
                    Quantity = 36,
                    ChildProductType = SealedProductType.BoosterPack,
                    ChildTemplateId = boosterTemplate.Id,
                }
            ]
        };
        ctx.Templates.Add(boxTemplate);
        ctx.SaveChanges();

        var loaded = ctx.Templates
            .Include(t => t.Contents)
            .ThenInclude(c => c.ChildTemplate)
            .First(t => t.Name == "Bloomburrow Booster Box");

        Assert.Equal("Bloomburrow Booster Box", loaded.Name);
        Assert.Single(loaded.Contents);
        Assert.Equal(36, loaded.Contents[0].Quantity);
        Assert.Equal("Bloomburrow Booster Pack", loaded.Contents[0].ChildTemplate!.Name);
    }

    [Fact]
    public void CanCreateInstance()
    {
        using var ctx = new SealedProductDbContext(_options);

        var template = new SealedProductTemplate
        {
            Name = "Test Pack",
            ProductType = SealedProductType.BoosterPack,
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();

        var instance = new SealedProductInstance
        {
            TemplateId = template.Id,
            PurchasePrice = 4.99m,
        };
        ctx.Instances.Add(instance);
        ctx.SaveChanges();

        var loaded = ctx.Instances.Include(i => i.Template).First();
        Assert.Equal("Test Pack", loaded.Template.Name);
        Assert.Equal(4.99m, loaded.PurchasePrice);
    }

    [Fact]
    public void ContentWithoutChildTemplate_StoresProductTypeOnly()
    {
        using var ctx = new SealedProductDbContext(_options);

        var bundle = new SealedProductTemplate
        {
            Name = "Test Bundle",
            ProductType = SealedProductType.BundleBox,
            Contents =
            [
                new SealedProductContents { Quantity = 8, ChildProductType = SealedProductType.BoosterPack },
                new SealedProductContents { Quantity = 1, ChildProductType = SealedProductType.PromoPack },
                new SealedProductContents { Quantity = 3, ChildProductType = SealedProductType.FixedPack },
            ]
        };
        ctx.Templates.Add(bundle);
        ctx.SaveChanges();

        var loaded = ctx.Templates.Include(t => t.Contents).First();
        Assert.Equal(3, loaded.Contents.Count);
        Assert.All(loaded.Contents, c => Assert.Null(c.ChildTemplateId));
    }

    [Fact]
    public void UpcIndex_IsUnique()
    {
        using var ctx = new SealedProductDbContext(_options);

        ctx.Templates.Add(new SealedProductTemplate
        {
            Name = "Product A",
            Upc = "123456789",
            ProductType = SealedProductType.BoosterBox,
        });
        ctx.SaveChanges();

        ctx.Templates.Add(new SealedProductTemplate
        {
            Name = "Product B",
            Upc = "123456789",
            ProductType = SealedProductType.BoosterBox,
        });

        Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
    }
}
