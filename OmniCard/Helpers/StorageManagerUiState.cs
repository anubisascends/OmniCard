using System.IO;
using System.Text.Json;
using OmniCard.Models;

namespace OmniCard.Helpers;

/// <summary>
/// Persists the Manage Storage Locations dialog's per-group collapse state to
/// <c>storage-manager-ui.json</c> in the data directory, so groups stay expanded/collapsed the way
/// the user left them between sessions. A corrupt or missing file is treated as "no saved state"
/// (everything defaults to expanded) rather than throwing.
/// </summary>
internal static class StorageManagerUiState
{
    private const string FileName = "storage-manager-ui.json";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Reads the saved expanded state keyed by <see cref="ContainerType"/> name.
    /// Missing entries mean "expanded".</summary>
    public static Dictionary<string, bool> LoadExpandedState(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
            return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    public static void SaveExpandedState(string dataDirectory, Dictionary<string, bool> state)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            var path = Path.Combine(dataDirectory, FileName);
            File.WriteAllText(path, JsonSerializer.Serialize(state, WriteOptions));
        }
        catch (Exception)
        {
            // Persisting UI state is best-effort; never let it break the dialog.
        }
    }
}
