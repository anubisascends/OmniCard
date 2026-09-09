namespace OmniCard.Shared.Collection;

public class CollectionPresetConfig
{
    public List<SortPreset> SortPresets { get; set; } = [];
    public List<FilterPreset> FilterPresets { get; set; } = [];
    public Dictionary<string, ActivePresetPair> ActivePresets { get; set; } = [];
}

public class ActivePresetPair
{
    public string? SortPreset { get; set; }
    public string? FilterPreset { get; set; }
}
