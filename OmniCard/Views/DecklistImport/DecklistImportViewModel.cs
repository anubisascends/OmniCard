using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.DecklistImport;

public sealed partial class DecklistImportViewModel(
    IDecklistService decklistService,
    ICardService cardService,
    IListService listService,
    IStorageContainerService containerService,
    ILogger<DecklistImportViewModel> logger) : ViewModel
{
    public ObservableCollection<DecklistImportRow> Rows { get; } = [];
    public ObservableCollection<CardList> AvailableLists { get; } = [];
    public ObservableCollection<StorageContainer> AvailableLocations { get; } = [];
    public IReadOnlyList<ContainerType> LocationTypes { get; } = Enum.GetValues<ContainerType>();

    [ObservableProperty] public partial string SourceName { get; set; } = "";
    [ObservableProperty] public partial string SummaryLabel { get; set; } = "";
    public int ResolvedCount { get; private set; }
    public int UnresolvedCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetIsLocation))]
    [NotifyPropertyChangedFor(nameof(TargetIsLocationEditable))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool TargetIsList { get; set; }

    public bool TargetIsLocation => !TargetIsList;

    /// <summary>Two-way alias so the "Location" radio can bind directly (radios need a settable source).</summary>
    public bool TargetIsLocationEditable
    {
        get => !TargetIsList;
        set => TargetIsList = !value;
    }

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))] public partial CardList? SelectedList { get; set; }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))] public partial StorageContainer? SelectedLocation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseExistingTarget))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool CreateNew { get; set; }

    public bool UseExistingTarget => !CreateNew;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(CanImport))] public partial string NewName { get; set; } = "";
    [ObservableProperty] public partial ContainerType NewLocationType { get; set; } = ContainerType.Box;

    public DecklistImportSummary? Result { get; private set; }
    public Action<bool>? CloseDialog { get; set; }

    public bool CanImport
    {
        get
        {
            if (ResolvedCount == 0) return false;
            if (CreateNew) return !string.IsNullOrWhiteSpace(NewName);
            return TargetIsList ? SelectedList is not null : SelectedLocation is not null;
        }
    }

    public void Load(string sourceName, string fileText, int? defaultContainerId)
    {
        SourceName = sourceName;
        var gs = cardService.ActiveGameService;
        var game = gs.Game;

        Rows.Clear();
        var entries = decklistService.ParseDecklistPrintings(fileText);
        foreach (var e in entries)
        {
            CardMatch? match;
            try
            {
                match = DecklistPrintingResolver.Resolve(gs, e);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve decklist entry {Name}", e.CardName);
                match = null;
            }
            Rows.Add(new DecklistImportRow
            {
                Quantity = e.Quantity,
                Name = e.CardName,
                SetCode = e.SetCode,
                CollectorNumber = e.CollectorNumber,
                Match = match,
            });
        }

        ResolvedCount = Rows.Count(r => r.IsResolved);
        UnresolvedCount = Rows.Count - ResolvedCount;
        SummaryLabel = $"{Rows.Count} lines · {ResolvedCount} resolved · {UnresolvedCount} unresolved";

        AvailableLists.Clear();
        foreach (var l in listService.GetLists(game))
            AvailableLists.Add(l);

        AvailableLocations.Clear();
        foreach (var c in containerService.GetAll())
            AvailableLocations.Add(c);

        // Default target: current Location if provided, else the Bulk container.
        // Always prefer the instance already in AvailableLocations (ComboBox selects by
        // reference) — only fall back to the raw GetBulk()/GetAll() instances if no
        // matching Id is found in the list at all.
        TargetIsList = false;
        var bulk = containerService.GetBulk();
        var bulkInList = AvailableLocations.FirstOrDefault(c => c.Id == bulk.Id);
        SelectedLocation = defaultContainerId is int id
            ? AvailableLocations.FirstOrDefault(c => c.Id == id) ?? bulkInList ?? bulk
            : bulkInList ?? bulk;

        OnPropertyChanged(nameof(CanImport));
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);

    [RelayCommand]
    public void Import()
    {
        var resolved = Rows.Where(r => r.IsResolved).ToList();
        var addedQty = 0;
        var unresolved = Rows.Count - resolved.Count;
        var game = cardService.ActiveGameService.Game;
        string targetName;

        if (TargetIsList)
        {
            var listId = CreateNew
                ? listService.CreateList(NewName.Trim(), game).Id
                : SelectedList!.Id;
            targetName = CreateNew ? NewName.Trim() : SelectedList!.Name;

            foreach (var row in resolved)
            {
                listService.AddPrinting(listId, row.Match!, isFoil: false, row.Quantity, ListItemSource.File);
                addedQty += row.Quantity;
            }
        }
        else
        {
            var container = CreateNew
                ? containerService.Create(NewName.Trim(), NewLocationType)
                : SelectedLocation!;
            targetName = container.Name;

            foreach (var row in resolved)
            {
                cardService.AddCardToCollection(row.Match!, game, condition: "Near Mint", isFoil: false,
                    purchasePrice: null, quantity: row.Quantity, container, page: null, slot: null, section: null);
                addedQty += row.Quantity;
            }
        }

        Result = new DecklistImportSummary(addedQty, unresolved, targetName);
        CloseDialog?.Invoke(true);
    }
}
