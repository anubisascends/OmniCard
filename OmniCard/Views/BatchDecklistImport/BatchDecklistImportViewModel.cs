using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Services;
using OmniCard.Views.DecklistImport;

namespace OmniCard.Views.BatchDecklistImport;

public sealed partial class BatchDecklistImportViewModel(
    IDecklistImportService importService,
    ICardService cardService,
    IListService listService,
    IStorageContainerService containerService,
    IDecklistService decklistService,
    ILogger<BatchDecklistImportViewModel> logger) : ViewModel
{
    public ObservableCollection<DecklistFileImport> Files { get; } = [];
    public ObservableCollection<CardList> AvailableLists { get; } = [];
    public ObservableCollection<StorageContainer> AvailableLocations { get; } = [];

    [ObservableProperty] public partial DecklistFileImport? SelectedFile { get; set; }
    [ObservableProperty] public partial string HeaderLabel { get; set; } = "";
    [ObservableProperty] public partial string UrlText { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    [NotifyPropertyChangedFor(nameof(CanImportNow))]
    public partial bool IsBusy { get; set; }

    public bool NotBusy => !IsBusy;

    public int CsvImportedCount { get; private set; }

    public bool CanImport => Files.Count > 0 && Files.All(f => f.HasTarget);
    public bool CanImportNow => CanImport && NotBusy;

    public BatchDecklistImportSummary? Result { get; private set; }
    public Action<bool>? CloseDialog { get; set; }

    /// <summary>Host callback (DialogService): open the OS file picker; return chosen paths or null.</summary>
    public Func<IReadOnlyList<string>?>? PickFiles { get; set; }
    /// <summary>Host callback (DialogService): run the CSV preview+import for a path; return imported count or null.</summary>
    public Func<string, int?>? ImportCsvFile { get; set; }

    public void Load()
    {
        var game = cardService.ActiveGameService.Game;
        AvailableLists.Clear();
        foreach (var l in listService.GetLists(game)) AvailableLists.Add(l);
        AvailableLocations.Clear();
        foreach (var c in containerService.GetAll()) AvailableLocations.Add(c);
        Files.Clear();
        UpdateHeader();
    }

    [RelayCommand]
    public void AddFiles()
    {
        var paths = PickFiles?.Invoke();
        if (paths is not null && paths.Count > 0) AddPaths(paths);
    }

    /// <summary>Classify + route each path: decklist → row, CSV → immediate import (callback), else skip.</summary>
    public void AddPaths(IReadOnlyList<string> paths)
    {
        var notes = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var kind = ImportFileClassifier.ClassifyFile(path);
                if (kind == ImportKind.Decklist)
                {
                    var rows = importService.ResolveFile(File.ReadAllText(path));
                    AddRow(Path.GetFileName(path), Path.GetFileNameWithoutExtension(path), rows);
                }
                else if (kind == ImportKind.Csv)
                {
                    var imported = ImportCsvFile?.Invoke(path);
                    if (imported.HasValue)
                    {
                        CsvImportedCount += imported.Value;
                        notes.Add($"Imported {imported.Value} from {Path.GetFileName(path)}");
                    }
                }
                else
                {
                    notes.Add($"Skipped {Path.GetFileName(path)} (unrecognized)");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to process import path {Path}", path);
                notes.Add($"Failed to process {Path.GetFileName(path)}");
            }
        }
        if (notes.Count > 0) StatusMessage = string.Join(" · ", notes);
        UpdateHeader();
    }

    [RelayCommand]
    public async Task AddUrls()
    {
        var urls = UrlText.Split('\n').Select(u => u.Trim()).Where(u => u.Length > 0).ToList();
        if (urls.Count == 0) { StatusMessage = "Paste one or more deck URLs (one per line)."; return; }

        IsBusy = true;
        StatusMessage = "Fetching…";
        try
        {
            var failed = new List<string>();
            foreach (var url in urls)
            {
                var fetched = await decklistService.FetchDecklistAsync(url);
                if (fetched is null) { failed.Add(url); continue; }
                var (deckName, entries) = fetched.Value;
                var rows = importService.ResolveEntries(entries);
                AddRow(deckName, deckName, rows);
            }
            UrlText = string.Join("\n", failed);
            var added = urls.Count - failed.Count;
            StatusMessage = failed.Count == 0
                ? $"Added {added} deck(s)."
                : $"Added {added} deck(s). Couldn't fetch: {string.Join(", ", failed)}";
        }
        finally
        {
            IsBusy = false;
        }
        UpdateHeader();
    }

    private void AddRow(string displayName, string defaultNewName, IReadOnlyList<DecklistImportRow> rows)
    {
        var item = new DecklistFileImport(displayName, defaultNewName, rows, AvailableLists, AvailableLocations);
        item.PropertyChanged += OnItemChanged;
        Files.Add(item);
        SelectedFile ??= item;
    }

    private void UpdateHeader()
    {
        HeaderLabel = $"{Files.Count} deck(s) · {Files.Sum(f => f.ResolvedCount)} resolved · {Files.Sum(f => f.UnresolvedCount)} unresolved";
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanImportNow));
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DecklistFileImport.HasTarget))
        {
            OnPropertyChanged(nameof(CanImport));
            OnPropertyChanged(nameof(CanImportNow));
        }
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

        Result = new BatchDecklistImportSummary(Files.Count, totalAdded, totalUnresolved, anyList, anyLocation, CsvImportedCount, perFile);
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel()
    {
        // Preserve CSVs imported during this session so the caller still refreshes its views.
        if (CsvImportedCount > 0)
            Result = new BatchDecklistImportSummary(0, 0, 0, false, false, CsvImportedCount, []);
        CloseDialog?.Invoke(false);
    }
}
