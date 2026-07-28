using Microsoft.Extensions.Logging;
using OmniCard.Collection;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.DecklistImport;

namespace OmniCard.Services;

public sealed class DecklistImportService(
    IDecklistService decklistService,
    ICardService cardService,
    IListService listService,
    ILogger<DecklistImportService> logger) : IDecklistImportService
{
    public IReadOnlyList<DecklistImportRow> ResolveFile(string fileText)
        => ResolveEntries(decklistService.ParseDecklistPrintings(fileText));

    public IReadOnlyList<DecklistImportRow> ResolveEntries(IEnumerable<DecklistEntry> entries)
    {
        var gs = cardService.ActiveGameService;
        var rows = new List<DecklistImportRow>();
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
            rows.Add(new DecklistImportRow
            {
                Quantity = e.Quantity,
                Name = e.CardName,
                SetCode = e.SetCode,
                CollectorNumber = e.CollectorNumber,
                Match = match,
            });
        }
        return rows;
    }

    public int CommitToList(int listId, IEnumerable<DecklistImportRow> resolvedRows)
    {
        var added = 0;
        foreach (var row in resolvedRows)
        {
            listService.AddPrinting(listId, row.Match!, isFoil: false, row.Quantity, ListItemSource.File);
            added += row.Quantity;
        }
        return added;
    }

    public int CommitToLocation(StorageContainer container, IEnumerable<DecklistImportRow> resolvedRows)
    {
        var added = 0;
        var game = cardService.ActiveGameService.Game;
        foreach (var row in resolvedRows)
        {
            cardService.AddCardToCollection(row.Match!, game, condition: "Near Mint", isFoil: false,
                purchasePrice: null, quantity: row.Quantity, container, page: null, slot: null, section: null);
            added += row.Quantity;
        }
        return added;
    }
}
