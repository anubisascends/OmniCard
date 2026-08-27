using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.DeckBoxSync;

/// <summary>Drives the "Upgrade Deck…" dialog: fetch/parse a target decklist, diff it against a deck box,
/// and apply the user's cut/add decisions as physical moves. MTG only for v1 (Moxfield/Archidekt).</summary>
public sealed partial class DeckBoxSyncViewModel(
    IDecklistService decklistService,
    IDeckBoxSyncService syncService,
    IStorageContainerService containerService,
    ILogger<DeckBoxSyncViewModel> logger) : ViewModel
{
    private const CardGame Game = CardGame.Mtg;

    private int _deckBoxId;

    public ObservableCollection<StorageContainer> AvailableLocations { get; } = [];
    public ObservableCollection<DeckBoxCutRowVm> Cuts { get; } = [];
    public ObservableCollection<DeckBoxAddRowVm> Adds { get; } = [];

    [ObservableProperty] public partial string HeaderLabel { get; set; } = "";
    [ObservableProperty] public partial string UrlText { get; set; } = "";
    [ObservableProperty] public partial string PasteText { get; set; } = "";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy))]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    public partial bool IsBusy { get; set; }

    public bool NotBusy => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit))]
    public partial bool HasPlan { get; set; }

    public bool CanCommit => HasPlan && NotBusy && Cuts.All(c => c.HasChoice);

    /// <summary>True when the user committed changes (so the caller can refresh the location view).</summary>
    public bool DidCommit { get; private set; }
    public Action<bool>? CloseDialog { get; set; }

    public void Load(StorageContainer deckBox)
    {
        _deckBoxId = deckBox.Id;
        HeaderLabel = $"Upgrade Deck — {deckBox.Name}";
        AvailableLocations.Clear();
        foreach (var c in containerService.GetAll().Where(c => c.Id != deckBox.Id))
            AvailableLocations.Add(c);
        Cuts.Clear();
        Adds.Clear();
        HasPlan = false;
        StatusMessage = "Paste a Moxfield/Archidekt URL, or paste a decklist, then load it.";
    }

    [RelayCommand]
    public async Task FetchAsync()
    {
        var url = UrlText.Trim();
        if (url.Length == 0) { StatusMessage = "Paste a Moxfield or Archidekt deck URL first."; return; }

        IsBusy = true;
        StatusMessage = "Fetching…";
        try
        {
            var fetched = await decklistService.FetchDecklistAsync(url);
            if (fetched is null)
            {
                StatusMessage = "Couldn't fetch that deck. Check the URL, or paste the decklist as text instead.";
                return;
            }
            BuildPlan(fetched.Value.Entries);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Deck box sync fetch failed for {Url}", url);
            StatusMessage = "Couldn't fetch that deck. Check the URL, or paste the decklist as text instead.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ParsePasted()
    {
        var text = PasteText;
        if (string.IsNullOrWhiteSpace(text)) { StatusMessage = "Paste a decklist first."; return; }
        var entries = decklistService.ParseDecklistPrintings(text);
        if (entries.Count == 0) { StatusMessage = "No cards found in that text."; return; }
        BuildPlan(entries);
    }

    private void BuildPlan(List<DecklistEntry> entries)
    {
        var plan = syncService.BuildPlan(_deckBoxId, entries, Game);
        Cuts.Clear();
        foreach (var c in plan.Cuts)
        {
            var row = new DeckBoxCutRowVm(c, AvailableLocations);
            // A row toggling between Sideboard/Move (or picking a location) can change whether every
            // row is resolved, so re-raise the commit gate whenever a cut row changes.
            row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanCommit));
            Cuts.Add(row);
        }
        Adds.Clear();
        foreach (var a in plan.Adds) Adds.Add(new DeckBoxAddRowVm(a));
        HasPlan = true;

        if (plan.Cuts.Count == 0 && plan.Adds.Count == 0)
            StatusMessage = $"Deck box already matches the list ({plan.KeepCount} cards).";
        else
            StatusMessage = $"{plan.TotalCut} to cut · {plan.TotalAdd} to add · {plan.KeepCount} unchanged.";
    }

    [RelayCommand]
    public void Commit()
    {
        if (!CanCommit) return;
        IsBusy = true;
        try
        {
            var request = new DeckBoxSyncCommitRequest(
                _deckBoxId,
                Cuts.Select(c => c.ToDecision()).ToList(),
                Adds.Select(a => a.ToDecision()).OfType<DeckBoxAddDecision>().ToList());
            syncService.ApplySync(request);
            DidCommit = true;
            CloseDialog?.Invoke(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deck box sync commit failed for container {Id}", _deckBoxId);
            StatusMessage = "Something went wrong applying the changes. Nothing further was moved.";
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
