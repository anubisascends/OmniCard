using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.DecklistCheck;

public sealed partial class DecklistCheckViewModel(
    IDecklistService decklistService,
    ILogger<DecklistCheckViewModel> logger) : ObservableObject
{
    [ObservableProperty]
    public partial string Url { get; set; } = "";

    [ObservableProperty]
    public partial string FallbackText { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowFallback { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial DecklistCheckResult? Result { get; set; }

    public Action<DecklistCheckResult>? ExportPdf { get; set; }

    [RelayCommand]
    public async Task Fetch()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            StatusMessage = "Please enter a URL.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Fetching decklist...";
        Result = null;

        try
        {
            var fetched = await decklistService.FetchDecklistAsync(Url.Trim());

            if (fetched is null)
            {
                ShowFallback = true;
                StatusMessage = "Couldn't reach the site. Paste your decklist below instead.";
                logger.LogWarning("Failed to fetch decklist from {Url}", Url);
                return;
            }

            var (deckName, entries) = fetched.Value;
            StatusMessage = $"Fetched \"{deckName}\" ({entries.Count} cards). Checking collection...";

            var source = Url.Contains("moxfield", StringComparison.OrdinalIgnoreCase) ? "Moxfield" : "Archidekt";
            Result = decklistService.CheckAgainstCollection(deckName, source, entries);
            StatusMessage = $"Owned: {Result.TotalOwned}/{Result.TotalCards} | Missing: {Result.TotalMissing} | Cost: ${Result.EstimatedCost:N2}";
            logger.LogInformation("Decklist check complete: {Owned}/{Total} owned, {Missing} missing",
                Result.TotalOwned, Result.TotalCards, Result.TotalMissing);
        }
        catch (Exception ex)
        {
            ShowFallback = true;
            StatusMessage = "Couldn't reach the site. Paste your decklist below instead.";
            logger.LogWarning(ex, "Error fetching decklist from {Url}", Url);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ParseText()
    {
        if (string.IsNullOrWhiteSpace(FallbackText))
        {
            StatusMessage = "Please paste a decklist.";
            return;
        }

        var (deckName, entries) = decklistService.ParseDecklistText(FallbackText);
        if (entries.Count == 0)
        {
            StatusMessage = "No cards found in the pasted text.";
            return;
        }

        StatusMessage = $"Parsed {entries.Count} cards. Checking collection...";
        Result = decklistService.CheckAgainstCollection(deckName, "Text", entries);
        StatusMessage = $"Owned: {Result.TotalOwned}/{Result.TotalCards} | Missing: {Result.TotalMissing} | Cost: ${Result.EstimatedCost:N2}";
    }

    [RelayCommand]
    public void GenerateReport()
    {
        if (Result is null) return;
        ExportPdf?.Invoke(Result);
    }
}
