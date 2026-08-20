using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class CardModel : PageModel
{
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;
    private readonly ICardService _cardService;
    private readonly IDataPathService _dataPathService;
    private readonly IDbContextFactory<PokemonDbContext>? _pokemonFactory;
    private readonly IDbContextFactory<YugiohDbContext>? _yugiohFactory;
    private readonly IDbContextFactory<FinalFantasyDbContext>? _finalFantasyFactory;
    private readonly IDbContextFactory<ScryfallDbContext>? _scryfallFactory;

    public CardModel(
        IDbContextFactory<OmniCardDbContext> dbFactory,
        ICardService cardService,
        IDataPathService dataPathService,
        IDbContextFactory<PokemonDbContext>? pokemonFactory = null,
        IDbContextFactory<YugiohDbContext>? yugiohFactory = null,
        IDbContextFactory<FinalFantasyDbContext>? finalFantasyFactory = null,
        IDbContextFactory<ScryfallDbContext>? scryfallFactory = null)
    {
        _dbFactory = dbFactory;
        _cardService = cardService;
        _dataPathService = dataPathService;
        _pokemonFactory = pokemonFactory;
        _yugiohFactory = yugiohFactory;
        _finalFantasyFactory = finalFantasyFactory;
        _scryfallFactory = scryfallFactory;
    }

    public CollectionCard Card { get; set; } = null!;

    /// <summary>
    /// Raw TCGCSV "extendedData" JSON for Pokémon/Yu-Gi-Oh!/Final Fantasy TCG cards, looked up
    /// live from the game's read-only catalog DB by <see cref="Product.GameCardId"/> (the
    /// TCGplayer productId). Not persisted on Product/CollectionCard — the owned-collection
    /// store has no column for it, so this is a display-only join. Null for other games or
    /// when no matching catalog row is found.
    /// </summary>
    public string? ExtendedDataJson { get; set; }

    /// <summary>Link to this card on TCGPlayer — the exact product when its TCGplayer id is known,
    /// otherwise a scoped name search. See <see cref="TcgPlayerLink"/>.</summary>
    public string TcgPlayerUrl { get; set; } = "";

    [TempData]
    public string? TradeMessage { get; set; }

    public IActionResult OnGet(int id)
    {
        using var db = _dbFactory.CreateDbContext();

        var lot = db.Lots
            .AsNoTracking()
            .Include(l => l.Product)
            .FirstOrDefault(l => l.Id == id && l.Product.Category == ProductCategory.Single);

        if (lot is null)
            return NotFound();

        var card = CollectionCardMapper.ToDto(lot, lot.Product, lot.Product.LastMarketPrice ?? 0m);

        if (lot.LocationId is int locationId)
            card.Container = db.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == locationId);

        card.Tags = TagLookup.GetTagsByLots(db, [card.Id]).GetValueOrDefault(card.Id, []);

        // Fill in catalog art when the card has no stored ImageUri, same as the desktop.
        CardArtHydrator.HydrateMissingImageUris(_cardService, [card]);
        // Singles don't persist a price on the row — look up the live catalog price.
        MarketPriceHydrator.Populate(_cardService, [card]);

        Card = card;
        ExtendedDataJson = LookupExtendedDataJson(card.Game, card.GameCardId);
        TcgPlayerUrl = BuildTcgPlayerUrl(card);
        return Page();
    }

    private string BuildTcgPlayerUrl(CollectionCard card)
    {
        // MTG stores a Scryfall id, not a TCGplayer id — resolve the real product id from scryfall.db.
        int? resolved = null;
        if (card.Game == CardGame.Mtg)
        {
            var etched = card.IsFoil && (card.FoilType?.Contains("Etched", StringComparison.OrdinalIgnoreCase) ?? false);
            resolved = ScryfallTcgIdResolver.Resolve(_scryfallFactory, [card.GameCardId])
                .GetValueOrDefault(card.GameCardId)
                .Pick(etched);
        }

        return TcgPlayerLink.Build(card.Game, card.GameCardId, card.Name, card.SetName, resolved);
    }

    private string? LookupExtendedDataJson(CardGame game, string gameCardId)
    {
        if (!int.TryParse(gameCardId, out var productId))
            return null;

        return game switch
        {
            CardGame.Pokemon => _pokemonFactory is null ? null : QueryExtendedData(_pokemonFactory, productId),
            CardGame.YuGiOh => _yugiohFactory is null ? null : QueryExtendedData(_yugiohFactory, productId),
            CardGame.FinalFantasy => _finalFantasyFactory is null ? null : QueryExtendedData(_finalFantasyFactory, productId),
            _ => null,
        };
    }

    private static string? QueryExtendedData<TContext>(IDbContextFactory<TContext> factory, int productId)
        where TContext : TcgCsvDbContext
    {
        try
        {
            using var db = factory.CreateDbContext();
            return db.Cards.AsNoTracking()
                .Where(c => c.ProductId == productId)
                .Select(c => c.ExtendedDataJson)
                .FirstOrDefault();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Catalog DB (pokemon.db/yugioh.db/fftcg.db) missing, locked, or corrupt — render
            // the card page without the extended-data section rather than 500ing.
            return null;
        }
    }

    public string? ImageUrl => CardImageUrl.Resolve(Card.ScanImagePath, Card.ImageUri, _dataPathService.ScansDirectory);
}
