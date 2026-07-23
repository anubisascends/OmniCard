using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Web.Pages;

public class CardModel : PageModel
{
    private readonly IDbContextFactory<OmniCardDbContext> _dbFactory;
    private readonly IDbContextFactory<PokemonDbContext>? _pokemonFactory;
    private readonly IDbContextFactory<YugiohDbContext>? _yugiohFactory;
    private readonly IDbContextFactory<FinalFantasyDbContext>? _finalFantasyFactory;

    public CardModel(
        IDbContextFactory<OmniCardDbContext> dbFactory,
        IDbContextFactory<PokemonDbContext>? pokemonFactory = null,
        IDbContextFactory<YugiohDbContext>? yugiohFactory = null,
        IDbContextFactory<FinalFantasyDbContext>? finalFantasyFactory = null)
    {
        _dbFactory = dbFactory;
        _pokemonFactory = pokemonFactory;
        _yugiohFactory = yugiohFactory;
        _finalFantasyFactory = finalFantasyFactory;
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

    public IActionResult OnGet(int id)
    {
        using var db = _dbFactory.CreateDbContext();

        var lot = db.Lots
            .AsNoTracking()
            .Include(l => l.Product)
            .FirstOrDefault(l => l.Id == id && l.Product.Category == ProductCategory.Single);

        if (lot is null)
            return NotFound();

        var card = CollectionCardMapper.ToDto(lot, lot.Product, 0m);

        if (lot.LocationId is int locationId)
            card.Container = db.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == locationId);

        Card = card;
        ExtendedDataJson = LookupExtendedDataJson(card.Game, card.GameCardId);
        return Page();
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

    public string? ImageUrl
    {
        get
        {
            if (Card.ScanImagePath is not null)
            {
                // ScanImagePath is "scans/123.jpg" — serve from /scans/123.jpg
                var filename = Path.GetFileName(Card.ScanImagePath);
                return $"/scans/{filename}";
            }
            return Card.ImageUri;
        }
    }
}
