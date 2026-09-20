using System.Text.Json;
using OmniCard.Shared.ImportExport;
using OmniCard.Shared.Settings;

namespace OmniCard.Collection.ImportExport;

/// <summary>File-backed <see cref="IOrderImportTemplateService"/>, persisting custom templates to
/// <c>order-import-templates.json</c> in the data directory (mirrors <c>ScanBadgeSettingsService</c>).
/// Built-in templates are merged in at read time and are never written to disk.</summary>
public sealed class OrderImportTemplateService : IOrderImportTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private readonly string _filePath;
    private readonly object _lock = new();

    public OrderImportTemplateService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "order-import-templates.json");
    }

    public IReadOnlyList<OrderImportTemplate> GetAll()
    {
        var custom = LoadCustom().OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase);
        return [.. OrderImportTemplate.BuiltIns(), .. custom];
    }

    public OrderImportTemplate? Get(string id)
        => GetAll().FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    public OrderImportTemplate Save(OrderImportTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
            throw new InvalidOperationException("A template name is required.");

        lock (_lock)
        {
            var custom = LoadCustom();

            // Editing an existing custom template (matched by id); otherwise it's a new one.
            var existing = string.IsNullOrWhiteSpace(template.Id)
                ? null
                : custom.FirstOrDefault(t => string.Equals(t.Id, template.Id, StringComparison.OrdinalIgnoreCase));

            if (existing is null && OrderImportTemplate.BuiltIns()
                    .Any(b => string.Equals(b.Id, template.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Built-in templates can't be overwritten.");

            var saved = new OrderImportTemplate
            {
                Id = existing?.Id ?? UniqueSlug(template.Name, custom),
                Name = template.Name.Trim(),
                IsBuiltIn = false,
                Channel = template.Channel,
                ColumnMappings = new Dictionary<string, string>(
                    template.ColumnMappings.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)),
                    StringComparer.Ordinal),
            };

            if (existing is not null) custom.Remove(existing);
            custom.Add(saved);
            SaveCustom(custom);
            return saved;
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            var custom = LoadCustom();
            var existing = custom.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null) return false;
            custom.Remove(existing);
            SaveCustom(custom);
            return true;
        }
    }

    private List<OrderImportTemplate> LoadCustom()
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            var list = JsonSerializer.Deserialize<List<OrderImportTemplate>>(File.ReadAllText(_filePath), JsonOptions)
                       ?? [];
            foreach (var t in list) t.IsBuiltIn = false; // never trust the flag on disk
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void SaveCustom(List<OrderImportTemplate> custom)
        => File.WriteAllText(_filePath, JsonSerializer.Serialize(custom, JsonOptions));

    private static string UniqueSlug(string name, IEnumerable<OrderImportTemplate> custom)
    {
        var baseSlug = new string(name.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray())
            .Trim('-');
        while (baseSlug.Contains("--")) baseSlug = baseSlug.Replace("--", "-");
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "template";

        var taken = new HashSet<string>(
            custom.Select(t => t.Id).Concat(OrderImportTemplate.BuiltIns().Select(b => b.Id)),
            StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(baseSlug)) return baseSlug;
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseSlug}-{i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
