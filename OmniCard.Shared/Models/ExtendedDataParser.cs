using System.Text.Json;

namespace OmniCard.Models;

/// <summary>
/// Parses a TcgCsvCard's raw ExtendedDataJson (TCGCSV "extendedData" array) into an ordered
/// display list of name/value pairs. Plain, non-UI helper so it can be shared by the desktop
/// (OmniCard.Controls.ExtendedDataView) and web card-detail panels without a WPF dependency.
/// </summary>
public static class ExtendedDataParser
{
    public static List<KeyValuePair<string, string>> Parse(string? json)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var name = el.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String
                    ? dn.GetString()
                    : el.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                var value = el.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (!string.IsNullOrEmpty(name))
                    result.Add(new KeyValuePair<string, string>(name!, value ?? ""));
            }
        }
        catch (JsonException) { /* malformed — show nothing */ }
        return result;
    }
}
