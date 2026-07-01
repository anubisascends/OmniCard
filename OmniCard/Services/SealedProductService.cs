using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Services;

public interface ISealedProductService
{
    List<SealedProductTemplate> GetTemplates();
    SealedProductTemplate? FindTemplateByUpc(string upc);
    SealedProductTemplate CreateTemplate(SealedProductTemplate template);
    void UpdateTemplate(SealedProductTemplate template);
    void DeleteTemplate(int templateId);
    List<SealedProductInstance> GetInstances();
    SealedProductInstance AddInstance(int templateId, decimal? purchasePrice);
    void DeleteInstance(int instanceId);
    SealedProductInstance? GetInstanceWithContents(int instanceId);
    List<SealedProductInstance> CrackInstance(int instanceId);
    List<SealedProductInstance> CrackInstanceWithTemplates(int instanceId, Dictionary<int, int> contentTemplateOverrides);
}

public class SealedProductService(IDbContextFactory<SealedProductDbContext> dbContextFactory) : ISealedProductService
{
    public List<SealedProductTemplate> GetTemplates()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Templates
            .AsNoTracking()
            .Include(t => t.Contents)
            .ThenInclude(c => c.ChildTemplate)
            .OrderBy(t => t.Name)
            .ToList();
    }

    public SealedProductTemplate? FindTemplateByUpc(string upc)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Templates
            .AsNoTracking()
            .Include(t => t.Contents)
            .FirstOrDefault(t => t.Upc == upc);
    }

    public SealedProductTemplate CreateTemplate(SealedProductTemplate template)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Templates.Add(template);
        ctx.SaveChanges();
        return template;
    }

    public void UpdateTemplate(SealedProductTemplate template)
    {
        using var ctx = dbContextFactory.CreateDbContext();

        // Remove old contents and replace
        var existing = ctx.TemplateContents.Where(c => c.TemplateId == template.Id);
        ctx.TemplateContents.RemoveRange(existing);

        ctx.Templates.Update(template);
        ctx.SaveChanges();
    }

    public void DeleteTemplate(int templateId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var template = ctx.Templates.Find(templateId);
        if (template is not null)
        {
            ctx.Templates.Remove(template);
            ctx.SaveChanges();
        }
    }

    public List<SealedProductInstance> GetInstances()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Instances
            .AsNoTracking()
            .Include(i => i.Template)
            .OrderByDescending(i => i.DateAdded)
            .ToList();
    }

    public SealedProductInstance? GetInstanceWithContents(int instanceId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Instances
            .AsNoTracking()
            .Include(i => i.Template)
            .ThenInclude(t => t.Contents)
            .ThenInclude(c => c.ChildTemplate)
            .FirstOrDefault(i => i.Id == instanceId);
    }

    public SealedProductInstance AddInstance(int templateId, decimal? purchasePrice)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var instance = new SealedProductInstance
        {
            TemplateId = templateId,
            PurchasePrice = purchasePrice,
        };
        ctx.Instances.Add(instance);
        ctx.SaveChanges();
        return instance;
    }

    public void DeleteInstance(int instanceId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var instance = ctx.Instances.Find(instanceId);
        if (instance is not null)
        {
            ctx.Instances.Remove(instance);
            ctx.SaveChanges();
        }
    }

    public List<SealedProductInstance> CrackInstance(int instanceId)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var parent = ctx.Instances
            .Include(i => i.Template)
            .ThenInclude(t => t.Contents)
            .FirstOrDefault(i => i.Id == instanceId);

        if (parent is null) return [];

        // Calculate total child count for even cost split
        var totalChildren = parent.Template.Contents.Sum(c => c.Quantity);
        var childPrice = totalChildren > 0 && parent.PurchasePrice.HasValue
            ? Math.Round(parent.PurchasePrice.Value / totalChildren, 2)
            : (decimal?)null;

        var children = new List<SealedProductInstance>();
        foreach (var content in parent.Template.Contents)
        {
            // Use child template if specified, otherwise create a generic template for this product type
            var childTemplateId = content.ChildTemplateId
                ?? GetOrCreateGenericTemplate(ctx, content.ChildProductType, parent.Template.SetCode);

            for (int i = 0; i < content.Quantity; i++)
            {
                var child = new SealedProductInstance
                {
                    TemplateId = childTemplateId,
                    PurchasePrice = childPrice,
                };
                children.Add(child);
            }
        }

        ctx.Instances.AddRange(children);
        ctx.Instances.Remove(parent);
        ctx.SaveChanges();

        return children;
    }

    public List<SealedProductInstance> CrackInstanceWithTemplates(int instanceId, Dictionary<int, int> contentTemplateOverrides)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var parent = ctx.Instances
            .Include(i => i.Template)
            .ThenInclude(t => t.Contents)
            .FirstOrDefault(i => i.Id == instanceId);

        if (parent is null) return [];

        var totalChildren = parent.Template.Contents.Sum(c => c.Quantity);
        var childPrice = totalChildren > 0 && parent.PurchasePrice.HasValue
            ? Math.Round(parent.PurchasePrice.Value / totalChildren, 2)
            : (decimal?)null;

        var children = new List<SealedProductInstance>();
        foreach (var content in parent.Template.Contents)
        {
            var childTemplateId = content.ChildTemplateId
                ?? (contentTemplateOverrides.TryGetValue(content.Id, out var overrideId) ? overrideId : 0);

            if (childTemplateId == 0)
                childTemplateId = GetOrCreateGenericTemplate(ctx, content.ChildProductType, parent.Template.SetCode);

            for (int i = 0; i < content.Quantity; i++)
            {
                children.Add(new SealedProductInstance
                {
                    TemplateId = childTemplateId,
                    PurchasePrice = childPrice,
                });
            }
        }

        ctx.Instances.AddRange(children);
        ctx.Instances.Remove(parent);
        ctx.SaveChanges();
        return children;
    }

    private static int GetOrCreateGenericTemplate(SealedProductDbContext ctx, SealedProductType productType, string? setCode)
    {
        var existing = ctx.Templates.FirstOrDefault(t =>
            t.ProductType == productType && t.SetCode == setCode && t.Upc == null);
        if (existing is not null) return existing.Id;

        var template = new SealedProductTemplate
        {
            Name = $"{setCode ?? "Generic"} {FormatProductType(productType)}",
            SetCode = setCode,
            ProductType = productType,
        };
        ctx.Templates.Add(template);
        ctx.SaveChanges();
        return template.Id;
    }

    private static string FormatProductType(SealedProductType type) => type switch
    {
        SealedProductType.Case => "Case",
        SealedProductType.BoosterBox => "Booster Box",
        SealedProductType.BundleBox => "Bundle Box",
        SealedProductType.CollectorBoosterPack => "Collector Booster Pack",
        SealedProductType.BoosterPack => "Booster Pack",
        SealedProductType.PromoPack => "Promo Pack",
        SealedProductType.FixedPack => "Fixed Pack",
        SealedProductType.Card => "Card",
        _ => type.ToString(),
    };
}
