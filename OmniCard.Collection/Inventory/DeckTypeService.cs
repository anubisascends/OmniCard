using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Storage;

namespace OmniCard.Collection.Inventory;

/// <summary>Per-game deck formats. Built-ins are seeded from <see cref="BuiltIns"/> (idempotent,
/// keyed on <see cref="DeckType.BuiltInKey"/>); users can add/rename/edit their own. Writes are
/// load-then-patch through the writable factory so the SQL Server rowversion path is respected.</summary>
public sealed class DeckTypeService(IDbContextFactory<OmniCardDbContext> dbContextFactory)
    : IDeckTypeService
{
    public void EnsureSeeded()
    {
        using var context = dbContextFactory.CreateDbContext();
        var existingKeys = context.DeckTypes
            .Where(d => d.BuiltInKey != null)
            .Select(d => d.BuiltInKey!)
            .ToHashSet();

        var toAdd = BuiltIns.Where(b => !existingKeys.Contains(b.BuiltInKey!)).ToList();
        if (toAdd.Count == 0)
            return;

        // Clone so the shared static definitions never get EF-tracked identities attached.
        foreach (var def in toAdd)
            context.DeckTypes.Add(Clone(def));
        context.SaveChanges();
    }

    public List<DeckType> GetForGame(CardGame game)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.DeckTypes.AsNoTracking()
            .Where(d => d.Game == game)
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Name)
            .ToList();
    }

    public DeckType? GetById(int id)
    {
        using var context = dbContextFactory.CreateDbContext();
        return context.DeckTypes.AsNoTracking().FirstOrDefault(d => d.Id == id);
    }

    public DeckType Create(DeckType deckType)
    {
        var name = (deckType.Name ?? "").Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Deck type name is required.");

        using var context = dbContextFactory.CreateDbContext();
        if (NameExists(context, deckType.Game, name, excludeId: null))
            throw new InvalidOperationException(
                $"A deck type named \"{name}\" already exists for this game.");

        var maxSort = context.DeckTypes.Where(d => d.Game == deckType.Game)
            .Select(d => (int?)d.SortOrder).Max() ?? 0;

        var created = Clone(deckType);
        created.Id = 0;
        created.Name = name;
        created.IsBuiltIn = false;   // user-created types are never built-ins
        created.BuiltInKey = null;
        created.SortOrder = maxSort + 1;

        context.DeckTypes.Add(created);
        context.SaveChanges();
        return created;
    }

    public void Update(int id, DeckType changes)
    {
        var name = (changes.Name ?? "").Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Deck type name is required.");

        using var context = dbContextFactory.CreateDbContext();
        var existing = context.DeckTypes.Find(id)
            ?? throw new InvalidOperationException($"Deck type {id} not found.");
        if (NameExists(context, existing.Game, name, excludeId: id))
            throw new InvalidOperationException(
                $"A deck type named \"{name}\" already exists for this game.");

        // Game is fixed for the life of the row; everything else (incl. rules) is editable.
        existing.Name = name;
        existing.DeckSizeMin = changes.DeckSizeMin;
        existing.DeckSizeMax = changes.DeckSizeMax;
        existing.MaxCopiesPerCard = changes.MaxCopiesPerCard;
        existing.Singleton = changes.Singleton;
        existing.BasicLandsExempt = changes.BasicLandsExempt;
        existing.CopiesCountByCollectorNumber = changes.CopiesCountByCollectorNumber;
        existing.CommanderSlots = changes.CommanderSlots;
        context.SaveChanges();
    }

    public void Delete(int id)
    {
        using var context = dbContextFactory.CreateDbContext();
        var existing = context.DeckTypes.Find(id)
            ?? throw new InvalidOperationException($"Deck type {id} not found.");
        // Deck boxes pointing at this type have DeckTypeId cleared by the FK's SetNull rule.
        context.DeckTypes.Remove(existing);
        context.SaveChanges();
    }

    private static bool NameExists(OmniCardDbContext context, CardGame game, string name, int? excludeId) =>
        context.DeckTypes.AsNoTracking()
            .Where(d => d.Game == game && (excludeId == null || d.Id != excludeId))
            .AsEnumerable()
            .Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    private static DeckType Clone(DeckType d) => new()
    {
        Game = d.Game,
        Name = d.Name,
        IsBuiltIn = d.IsBuiltIn,
        BuiltInKey = d.BuiltInKey,
        SortOrder = d.SortOrder,
        DeckSizeMin = d.DeckSizeMin,
        DeckSizeMax = d.DeckSizeMax,
        MaxCopiesPerCard = d.MaxCopiesPerCard,
        Singleton = d.Singleton,
        BasicLandsExempt = d.BasicLandsExempt,
        CopiesCountByCollectorNumber = d.CopiesCountByCollectorNumber,
        CommanderSlots = d.CommanderSlots,
    };

    /// <summary>Built-in deck formats seeded per game. Magic is the varied one (Commander is the
    /// singleton-with-commander outlier); the other games get their handful of real formats. Rule
    /// fields are best-effort defaults the user can edit — they drive warnings only, never blocks.</summary>
    internal static readonly IReadOnlyList<DeckType> BuiltIns = BuildBuiltIns();

    private static List<DeckType> BuildBuiltIns()
    {
        var list = new List<DeckType>();
        int sort;

        DeckType Def(CardGame game, string key, string name, int s,
            int? sizeMin = null, int? sizeMax = null, int? maxCopies = null,
            bool singleton = false, bool basicsExempt = false, int commanderSlots = 0,
            bool copiesByNumber = false) => new()
        {
            Game = game, BuiltInKey = key, Name = name, IsBuiltIn = true, SortOrder = s,
            DeckSizeMin = sizeMin, DeckSizeMax = sizeMax, MaxCopiesPerCard = maxCopies,
            Singleton = singleton, BasicLandsExempt = basicsExempt, CommanderSlots = commanderSlots,
            CopiesCountByCollectorNumber = copiesByNumber,
        };

        // --- Magic: The Gathering ---
        sort = 0;
        list.Add(Def(CardGame.Mtg, "mtg.commander", "Commander", sort++, 100, 100, 1, singleton: true, basicsExempt: true, commanderSlots: 1));
        list.Add(Def(CardGame.Mtg, "mtg.standard", "Standard", sort++, 60, null, 4));
        list.Add(Def(CardGame.Mtg, "mtg.pioneer", "Pioneer", sort++, 60, null, 4));
        list.Add(Def(CardGame.Mtg, "mtg.modern", "Modern", sort++, 60, null, 4));
        list.Add(Def(CardGame.Mtg, "mtg.legacy", "Legacy", sort++, 60, null, 4));
        list.Add(Def(CardGame.Mtg, "mtg.vintage", "Vintage", sort++, 60, null, 4));
        list.Add(Def(CardGame.Mtg, "mtg.pauper", "Pauper", sort++, 60, null, 4));
        list.Add(Def(CardGame.Mtg, "mtg.brawl", "Brawl", sort++, 60, 60, 1, singleton: true, basicsExempt: true, commanderSlots: 1));
        list.Add(Def(CardGame.Mtg, "mtg.oathbreaker", "Oathbreaker", sort++, 60, 60, 1, singleton: true, basicsExempt: true, commanderSlots: 2));
        list.Add(Def(CardGame.Mtg, "mtg.limited", "Limited / Draft", sort++, 40, null, null));
        list.Add(Def(CardGame.Mtg, "mtg.cube", "Cube", sort++));

        // --- One Piece TCG (Leader + 50-card deck, max 4 per card number) ---
        // Copies count by card number (set code), so alternate arts of the same card don't stack.
        sort = 0;
        list.Add(Def(CardGame.OnePiece, "optcg.constructed", "Constructed", sort++, 50, 50, 4, commanderSlots: 1, copiesByNumber: true));

        // --- Riftbound (Legend/Champion-based) ---
        sort = 0;
        list.Add(Def(CardGame.Riftbound, "riftbound.constructed", "Constructed", sort++, null, null, 3, commanderSlots: 1));

        // --- Pokémon (60-card, max 4) ---
        sort = 0;
        list.Add(Def(CardGame.Pokemon, "pokemon.standard", "Standard", sort++, 60, 60, 4));
        list.Add(Def(CardGame.Pokemon, "pokemon.expanded", "Expanded", sort++, 60, 60, 4));
        list.Add(Def(CardGame.Pokemon, "pokemon.unlimited", "Unlimited", sort++, 60, 60, 4));

        // --- Yu-Gi-Oh! (40–60 main deck, max 3) ---
        sort = 0;
        list.Add(Def(CardGame.YuGiOh, "yugioh.advanced", "Advanced", sort++, 40, 60, 3));
        list.Add(Def(CardGame.YuGiOh, "yugioh.traditional", "Traditional", sort++, 40, 60, 3));

        // --- Final Fantasy TCG (50-card, max 3) ---
        sort = 0;
        list.Add(Def(CardGame.FinalFantasy, "fftcg.standard", "Standard", sort++, 50, 50, 3));
        list.Add(Def(CardGame.FinalFantasy, "fftcg.classic", "Classic", sort++, 50, 50, 3));

        return list;
    }
}
