using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.BatchDecklistImport;

public sealed partial class BatchDecklistImportViewModel(
    IDecklistImportService importService,
    ICardService cardService,
    IListService listService,
    IStorageContainerService containerService) : ViewModel
{
    public ObservableCollection<DecklistFileImport> Files { get; } = [];
    public ObservableCollection<CardList> AvailableLists { get; } = [];
    public ObservableCollection<StorageContainer> AvailableLocations { get; } = [];

    [ObservableProperty] public partial DecklistFileImport? SelectedFile { get; set; }
    [ObservableProperty] public partial string HeaderLabel { get; set; } = "";

    public bool CanImport => Files.Count > 0 && Files.All(f => f.HasTarget);

    public BatchDecklistImportSummary? Result { get; private set; }
    public Action<bool>? CloseDialog { get; set; }

    public void Load(IReadOnlyList<(string Name, string Text)> files)
    {
        var game = cardService.ActiveGameService.Game;

        AvailableLists.Clear();
        foreach (var l in listService.GetLists(game)) AvailableLists.Add(l);
        AvailableLocations.Clear();
        foreach (var c in containerService.GetAll()) AvailableLocations.Add(c);

        Files.Clear();
        foreach (var (name, text) in files)
        {
            var rows = importService.ResolveFile(text);
            var item = new DecklistFileImport(name, rows, AvailableLists, AvailableLocations);
            item.PropertyChanged += OnItemChanged;
            Files.Add(item);
        }

        SelectedFile = Files.FirstOrDefault();
        HeaderLabel = $"{Files.Count} files · {Files.Sum(f => f.ResolvedCount)} resolved · {Files.Sum(f => f.UnresolvedCount)} unresolved";
        OnPropertyChanged(nameof(CanImport));
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DecklistFileImport.HasTarget))
            OnPropertyChanged(nameof(CanImport));
    }

    [RelayCommand]
    public void Import()
    {
        var game = cardService.ActiveGameService.Game;
        var perFile = new List<BatchFileResult>();
        var totalAdded = 0;
        var totalUnresolved = 0;
        var anyList = false;
        var anyLocation = false;

        foreach (var f in Files)
        {
            var resolved = f.Rows.Where(r => r.IsResolved).ToList();
            int added;
            string targetName;

            if (f.TargetIsList)
            {
                anyList = true;
                var listId = f.CreateNew ? listService.CreateList(f.NewName.Trim(), game).Id : f.SelectedList!.Id;
                targetName = f.CreateNew ? f.NewName.Trim() : f.SelectedList!.Name;
                added = importService.CommitToList(listId, resolved);
            }
            else
            {
                anyLocation = true;
                var container = f.CreateNew ? containerService.Create(f.NewName.Trim(), f.NewLocationType) : f.SelectedLocation!;
                targetName = container.Name;
                added = importService.CommitToLocation(container, resolved);
            }

            totalAdded += added;
            totalUnresolved += f.UnresolvedCount;
            perFile.Add(new BatchFileResult(f.SourceName, targetName, added, f.UnresolvedCount));
        }

        Result = new BatchDecklistImportSummary(Files.Count, totalAdded, totalUnresolved, anyList, anyLocation, perFile);
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
