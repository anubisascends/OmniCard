using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Labels;

namespace OmniCard.Web.Services.Labels;

/// <summary>A label template failed validation; maps to HTTP 400.</summary>
public sealed class LabelTemplateException(IReadOnlyList<string> errors) : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// The shared label templates (Settings ▸ Labels). A template is the sheet layout the user typed in
/// plus the pdfme design of one label; the server only checks that the layout leaves room for the
/// labels and that the design is a pdfme template object — rendering happens in the SPA.
/// </summary>
public sealed class LabelTemplateService(IDbContextFactory<OmniCardDbContext> dbFactory, TimeProvider time)
{
    public IReadOnlyList<LabelTemplateDto> List()
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.LabelTemplates.AsNoTracking().ToList()
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto).ToList();
    }

    public LabelTemplateDto? Get(int id)
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.LabelTemplates.AsNoTracking().FirstOrDefault(t => t.Id == id) is { } t ? ToDto(t) : null;
    }

    public LabelTemplateDto Create(LabelTemplateInput input)
    {
        using var ctx = dbFactory.CreateDbContext();
        var template = new LabelTemplate { CreatedAt = time.GetUtcNow().UtcDateTime };
        Apply(ctx, template, input);
        ctx.LabelTemplates.Add(template);
        ctx.SaveChanges();
        return ToDto(template);
    }

    /// <summary>Updates a template (load-then-patch); null when it doesn't exist.</summary>
    public LabelTemplateDto? Update(int id, LabelTemplateInput input)
    {
        using var ctx = dbFactory.CreateDbContext();
        if (ctx.LabelTemplates.FirstOrDefault(t => t.Id == id) is not { } template) return null;
        Apply(ctx, template, input);
        ctx.SaveChanges();
        return ToDto(template);
    }

    public bool Delete(int id)
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.LabelTemplates.Where(t => t.Id == id).ExecuteDelete() > 0;
    }

    /// <summary>Problems with <paramref name="input"/> (empty = valid). Name uniqueness is checked on save.</summary>
    public static IReadOnlyList<string> Validate(LabelTemplateInput input)
    {
        var errors = new List<string>();
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0) errors.Add("Enter a name.");
        else if (name.Length > LabelTemplate.MaxNameLength)
            errors.Add($"The name is too long (max {LabelTemplate.MaxNameLength} characters).");

        if (!InRange(input.PageWidthMm, LabelTemplate.MinPageMm, LabelTemplate.MaxPageMm)
            || !InRange(input.PageHeightMm, LabelTemplate.MinPageMm, LabelTemplate.MaxPageMm))
            errors.Add($"The page must be between {LabelTemplate.MinPageMm:0} and {LabelTemplate.MaxPageMm:0} mm on each side.");

        double[] spacing = [input.MarginTopMm, input.MarginRightMm, input.MarginBottomMm, input.MarginLeftMm, input.ColumnGapMm, input.RowGapMm];
        if (spacing.Any(v => !InRange(v, 0, LabelTemplate.MaxPageMm)))
            errors.Add("Margins and spacing can't be negative.");

        if (input.Columns is < 1 or > LabelTemplate.MaxGrid || input.Rows is < 1 or > LabelTemplate.MaxGrid)
            errors.Add($"Columns and rows must each be between 1 and {LabelTemplate.MaxGrid}.");
        else if (errors.Count == 0)
        {
            var w = LabelTemplate.LabelWidth(input.PageWidthMm, input.MarginLeftMm, input.MarginRightMm, input.Columns, input.ColumnGapMm);
            var h = LabelTemplate.LabelHeight(input.PageHeightMm, input.MarginTopMm, input.MarginBottomMm, input.Rows, input.RowGapMm);
            if (w < 1 || h < 1)
                errors.Add("The margins and spacing leave no room for the labels — reduce them, or use fewer columns or rows.");
        }

        var design = input.Design ?? "";
        if (design.Length > LabelTemplate.MaxDesignLength)
            errors.Add("The label design is too large — use smaller images.");
        else if (!IsDesign(design))
            errors.Add("The label design isn't valid.");

        return errors;
    }

    private void Apply(OmniCardDbContext ctx, LabelTemplate template, LabelTemplateInput input)
    {
        var errors = Validate(input).ToList();
        var name = input.Name?.Trim() ?? "";
        // Name match in memory: SQLite (tests) compares case-sensitively, SQL Server doesn't.
        if (name.Length > 0 && ctx.LabelTemplates.Where(t => t.Id != template.Id).Select(t => t.Name).AsEnumerable()
                .Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
            errors.Add($"A label template named '{name}' already exists.");
        if (errors.Count > 0) throw new LabelTemplateException(errors);

        template.Name = name;
        template.PageWidthMm = input.PageWidthMm;
        template.PageHeightMm = input.PageHeightMm;
        template.MarginTopMm = input.MarginTopMm;
        template.MarginRightMm = input.MarginRightMm;
        template.MarginBottomMm = input.MarginBottomMm;
        template.MarginLeftMm = input.MarginLeftMm;
        template.Columns = input.Columns;
        template.Rows = input.Rows;
        template.ColumnGapMm = input.ColumnGapMm;
        template.RowGapMm = input.RowGapMm;
        template.Design = input.Design;
        template.UpdatedAt = time.GetUtcNow().UtcDateTime;
    }

    private static bool InRange(double v, double min, double max) => double.IsFinite(v) && v >= min && v <= max;

    /// <summary>A pdfme template object: <c>schemas</c> is an array (of pages, each an array of elements).</summary>
    private static bool IsDesign(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("schemas", out var schemas)
                && schemas.ValueKind == JsonValueKind.Array
                && schemas.EnumerateArray().All(p => p.ValueKind == JsonValueKind.Array);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static LabelTemplateDto ToDto(LabelTemplate t) => new(
        t.Id, t.Name, t.PageWidthMm, t.PageHeightMm,
        t.MarginTopMm, t.MarginRightMm, t.MarginBottomMm, t.MarginLeftMm,
        t.Columns, t.Rows, t.ColumnGapMm, t.RowGapMm,
        Math.Round(t.LabelWidthMm, 2), Math.Round(t.LabelHeightMm, 2),
        t.Design, t.UpdatedAt);
}
