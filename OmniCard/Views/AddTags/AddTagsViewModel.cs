using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;

namespace OmniCard.Views.AddTags;

/// <summary>Prompts for one or more tags to apply to a bulk selection of cards — reuses the same
/// TagEditor control as the single-card editor/scan panel, just without a starting set.</summary>
public sealed partial class AddTagsViewModel(ITagService tagService) : ViewModel
{
    public ObservableCollection<string> Tags { get; } = [];
    public ObservableCollection<string> AllTagSuggestions { get; } = [];

    public Action<bool>? CloseDialog { get; set; }

    public List<string> Result { get; private set; } = [];

    public void Load()
    {
        Tags.Clear();
        AllTagSuggestions.Clear();
        foreach (var tag in tagService.GetAllTags())
            AllTagSuggestions.Add(tag.Name);
    }

    [RelayCommand]
    public void Confirm()
    {
        Result = Tags.ToList();
        CloseDialog?.Invoke(Result.Count > 0);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
