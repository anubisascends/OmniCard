using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;

namespace OmniCard.Views.CreateListFromScans;

/// <summary>One game's group of scans, plus the user's chosen list destination (existing or new).</summary>
public sealed partial class ScanListTargetRow : ObservableObject
{
    public ScanListTargetRow(CardGame game, int count, IReadOnlyList<CardList> availableLists, string defaultNewName)
    {
        Game = game;
        Count = count;
        AvailableLists = availableLists;
        NewName = defaultNewName;
        CreateNew = availableLists.Count == 0;
        SelectedList = availableLists.Count > 0 ? availableLists[0] : null;
    }

    public CardGame Game { get; }
    public int Count { get; }
    public IReadOnlyList<CardList> AvailableLists { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    public partial CardList? SelectedList { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseExisting))]
    [NotifyPropertyChangedFor(nameof(UseExistingEditable))]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    public partial bool CreateNew { get; set; }

    public bool UseExisting => !CreateNew;

    /// <summary>Writable mirror of <see cref="UseExisting"/> for the "Existing list" RadioButton's
    /// IsChecked binding (WPF requires a settable property for TwoWay bindings).</summary>
    public bool UseExistingEditable { get => !CreateNew; set => CreateNew = !value; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    public partial string NewName { get; set; } = "";

    public bool HasTarget => CreateNew ? !string.IsNullOrWhiteSpace(NewName) : SelectedList is not null;
}
