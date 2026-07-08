using OmniCard.Models;

namespace OmniCard.Interfaces;

public interface ICollectionPresetService
{
    List<SortPreset> GetSortPresets(CardGame game);
    void SaveSortPreset(SortPreset preset);
    void DeleteSortPreset(string name, CardGame game);
    List<FilterPreset> GetFilterPresets(CardGame game);
    void SaveFilterPreset(FilterPreset preset);
    void DeleteFilterPreset(string name, CardGame game);
    void SetActiveSortPreset(CardGame game, string? name);
    void SetActiveFilterPreset(CardGame game, string? name);
    SortPreset? GetActiveSortPreset(CardGame game);
    FilterPreset? GetActiveFilterPreset(CardGame game);
}
