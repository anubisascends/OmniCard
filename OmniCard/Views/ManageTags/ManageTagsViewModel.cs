using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;

namespace OmniCard.Views.ManageTags;

public sealed partial class ManageTagsViewModel(ITagService tagService) : ViewModel
{
    public ObservableCollection<TagDisplayItem> Tags { get; } = [];

    [ObservableProperty]
    public partial TagDisplayItem? SelectedTag { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditName { get; set; } = "";

    [ObservableProperty]
    public partial bool IsMerging { get; set; }

    [ObservableProperty]
    public partial TagDisplayItem? MergeTarget { get; set; }

    public Action? CloseDialog { get; set; }

    public ObservableCollection<TagDisplayItem> MergeCandidates { get; } = [];

    partial void OnSelectedTagChanged(TagDisplayItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
    }

    public bool HasSelection => SelectedTag is not null;

    public void Load()
    {
        var previousSelectedId = SelectedTag?.Id;
        Tags.Clear();
        foreach (var t in tagService.GetAllTags())
            Tags.Add(new TagDisplayItem { Id = t.Id, Name = t.Name, UsageCount = t.UsageCount });

        if (previousSelectedId is not null)
            SelectedTag = Tags.FirstOrDefault(t => t.Id == previousSelectedId);
    }

    [RelayCommand]
    public void ShowEdit()
    {
        if (SelectedTag is null) return;
        IsEditing = true;
        IsMerging = false;
        EditName = SelectedTag.Name;
    }

    [RelayCommand]
    public void ConfirmEdit()
    {
        if (SelectedTag is null || string.IsNullOrWhiteSpace(EditName)) return;
        tagService.RenameTag(SelectedTag.Id, EditName.Trim());
        IsEditing = false;
        Load();
    }

    [RelayCommand]
    public void CancelEdit() => IsEditing = false;

    [RelayCommand]
    public void DeleteSelected()
    {
        if (SelectedTag is null) return;

        var confirm = System.Windows.MessageBox.Show(
            $"Delete tag \"{SelectedTag.Name}\"? It will be removed from {SelectedTag.UsageCount} card(s). This cannot be undone.",
            "Delete Tag",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        tagService.DeleteTag(SelectedTag.Id);
        SelectedTag = null;
        Load();
    }

    [RelayCommand]
    public void ShowMerge()
    {
        if (SelectedTag is null) return;
        IsMerging = true;
        IsEditing = false;
        MergeCandidates.Clear();
        foreach (var t in Tags.Where(t => t.Id != SelectedTag.Id))
            MergeCandidates.Add(t);
        MergeTarget = null;
    }

    [RelayCommand]
    public void ConfirmMerge()
    {
        if (SelectedTag is null || MergeTarget is null) return;

        var confirm = System.Windows.MessageBox.Show(
            $"Merge \"{SelectedTag.Name}\" into \"{MergeTarget.Name}\"? Every card tagged \"{SelectedTag.Name}\" will be tagged \"{MergeTarget.Name}\" instead, and \"{SelectedTag.Name}\" will be deleted.",
            "Merge Tags",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        tagService.MergeTags(SelectedTag.Id, MergeTarget.Id);
        IsMerging = false;
        SelectedTag = null;
        Load();
    }

    [RelayCommand]
    public void CancelMerge() => IsMerging = false;

    [RelayCommand]
    public void Close() => CloseDialog?.Invoke();
}

public class TagDisplayItem
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int UsageCount { get; init; }
}
