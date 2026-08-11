using System.IO;
using System.Text.Json;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class ScannerSettingsService : IScannerSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public ScannerSettingsService(IDataPathService dataPathService)
    {
        _filePath = Path.Combine(dataPathService.DataDirectory, "scanner-settings.json");
    }

    public ScanWorkflowMode WorkflowMode => Load().WorkflowMode;

    public void SetWorkflowMode(ScanWorkflowMode mode)
    {
        var settings = Load();
        settings.WorkflowMode = mode;
        Save(settings);
    }

    private ScannerSettings Load()
    {
        if (!File.Exists(_filePath))
            return new ScannerSettings();

        try
        {
            return JsonSerializer.Deserialize<ScannerSettings>(File.ReadAllText(_filePath), JsonOptions)
                   ?? new ScannerSettings();
        }
        catch (JsonException)
        {
            return new ScannerSettings();
        }
    }

    private void Save(ScannerSettings settings)
        => File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
}
