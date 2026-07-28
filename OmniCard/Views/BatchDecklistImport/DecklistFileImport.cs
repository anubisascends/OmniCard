using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;
using OmniCard.Views.DecklistImport;

namespace OmniCard.Views.BatchDecklistImport;

/// <summary>One decklist file in the batch: its resolved rows plus its chosen target.</summary>
public sealed partial class DecklistFileImport : ObservableObject
{
    public DecklistFileImport(
        string sourceName,
        IReadOnlyList<DecklistImportRow> rows,
        IReadOnlyList<CardList> availableLists,
        IReadOnlyList<StorageContainer> availableLocations)
    {
        SourceName = sourceName;
        DefaultNewName = Path.GetFileNameWithoutExtension(sourceName);
        NewName = DefaultNewName;
        AvailableLists = availableLists;
        AvailableLocations = availableLocations;
        foreach (var r in rows) Rows.Add(r);
        ResolvedCount = Rows.Count(r => r.IsResolved);
        UnresolvedCount = Rows.Count - ResolvedCount;
        SummaryLabel = $"{ResolvedCount} resolved · {UnresolvedCount} unresolved";
    }

    public string SourceName { get; }
    public string DefaultNewName { get; }
    public ObservableCollection<DecklistImportRow> Rows { get; } = [];
    public int ResolvedCount { get; }
    public int UnresolvedCount { get; }
    public string SummaryLabel { get; }
    public IReadOnlyList<CardList> AvailableLists { get; }
    public IReadOnlyList<StorageContainer> AvailableLocations { get; }
    public IReadOnlyList<ContainerType> LocationTypes { get; } = Enum.GetValues<ContainerType>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetIsLocation))]
    [NotifyPropertyChangedFor(nameof(TargetIsLocationEditable))]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    public partial bool TargetIsList { get; set; }

    public bool TargetIsLocation => !TargetIsList;
    public bool TargetIsLocationEditable { get => !TargetIsList; set => TargetIsList = !value; }

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasTarget))] public partial CardList? SelectedList { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasTarget))] public partial StorageContainer? SelectedLocation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseExistingTarget))]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    public partial bool CreateNew { get; set; }

    public bool UseExistingTarget => !CreateNew;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasTarget))] public partial string NewName { get; set; } = "";
    [ObservableProperty] public partial ContainerType NewLocationType { get; set; } = ContainerType.Box;

    public bool HasTarget
    {
        get
        {
            if (CreateNew) return !string.IsNullOrWhiteSpace(NewName);
            return TargetIsList ? SelectedList is not null : SelectedLocation is not null;
        }
    }
}
