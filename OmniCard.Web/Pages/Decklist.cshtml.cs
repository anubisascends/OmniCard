using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class DecklistModel : PageModel
{
    private readonly IDecklistService _decklistService;

    public DecklistModel(IDecklistService decklistService)
    {
        _decklistService = decklistService;
    }

    [BindProperty]
    public string? DeckUrl { get; set; }

    public DecklistCheckResult? Result { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(DeckUrl))
        {
            ErrorMessage = "Please enter a deck URL.";
            return Page();
        }

        try
        {
            var fetched = await _decklistService.FetchDecklistAsync(DeckUrl.Trim());
            if (fetched is null)
            {
                ErrorMessage = "Could not parse URL. Supported sites: Moxfield, Archidekt.";
                return Page();
            }

            var (deckName, entries) = fetched.Value;
            if (entries.Count == 0)
            {
                ErrorMessage = "No cards found in decklist.";
                return Page();
            }

            var source = DeckUrl.Contains("moxfield", StringComparison.OrdinalIgnoreCase)
                ? "Moxfield" : "Archidekt";
            Result = _decklistService.CheckAgainstCollection(deckName, source, entries);
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Failed to fetch decklist. Check the URL and try again.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }

        return Page();
    }

    public IEnumerable<IGrouping<string, OwnedDecklistEntry>> OwnedByType =>
        (Result?.OwnedEntries ?? [])
            .GroupBy(e => e.TypeCategory ?? "Other")
            .OrderBy(g => Array.IndexOf(DecklistService.TypeCategoryOrder, g.Key));

    public IEnumerable<IGrouping<string, MissingDecklistEntry>> MissingByType =>
        (Result?.MissingEntries ?? [])
            .GroupBy(e => e.TypeCategory ?? "Other")
            .OrderBy(g => Array.IndexOf(DecklistService.TypeCategoryOrder, g.Key));
}
