using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Shared.BuyList;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Controllers;
using OmniCard.Web.Api.Mapping;

namespace OmniCard.Web.Services.BuyList;

/// <summary>A Buy List request that can't be done; maps to HTTP <see cref="StatusCode"/>.</summary>
public sealed class BuyListException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>One card or stack checked against the Buy List. A <see cref="Quantity"/> of 0 is still flagged
/// but claims no copies (a card left out of an intake quote).</summary>
public sealed record BuyListCandidate(CardGame Game, string Name, string GameCardId, bool IsFoil, int Quantity);

/// <summary>What the Buy List says about one candidate: <see cref="Hit"/> when any entry matches, and
/// <see cref="Copies"/> of it still wanted, taken from the entries in <see cref="Takes"/>.</summary>
public sealed record BuyListAllocation(BuyListHitDto? Hit, int Copies, IReadOnlyList<(int EntryId, int Copies)> Takes)
{
    public static readonly BuyListAllocation None = new(null, 0, []);
}

/// <summary>
/// The per-game Buy Lists: the admin's edits, the flag on scanned and quoted cards, and the count-down as
/// copies come in. Copies are <see cref="Allocate">allocated</see> in order across a commit or quote, so a
/// card wanted twice only claims two of the five copies scanned, and only those get the Buy List rate and
/// the set-aside location. <see cref="Consume"/> then subtracts them, clamped in case the list moved since.
/// Singleton, so its lock covers every count-down.
/// </summary>
public sealed class BuyListService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IBuyListSettingsService settingsService,
    ILogger<BuyListService> logger)
{
    private readonly object _countLock = new();

    public BuyListPageDto List(CardGame? game, bool fulfilled, IReadOnlyCollection<int>? siteIds)
    {
        using var ctx = dbFactory.CreateDbContext();
        var scope = ctx.BuyListEntries.AsNoTracking().AsQueryable();
        if (game is { } g) scope = scope.Where(e => e.Game == g);
        var rows = (fulfilled ? scope.Where(e => e.WantedQuantity == 0) : scope.Where(e => e.WantedQuantity > 0)).ToList();

        var owned = fulfilled ? null : OwnedCopies(ctx, game, siteIds);
        var items = rows
            .OrderByDescending(e => e.Priority).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id)
            .Select(e => ToDto(e) with
            {
                OwnedCopies = owned?.Where(o => o.Game == e.Game && BuyListMatcher.Matches(e, o.Name, o.GameCardId, o.IsFoil))
                    .Sum(o => o.Quantity) ?? 0,
            })
            .ToList();

        var setAside = game is { } one ? SetAside(ctx, one) : null;
        return new BuyListPageDto
        {
            Items = items,
            ActiveCount = fulfilled ? scope.Count(e => e.WantedQuantity > 0) : items.Count,
            FulfilledCount = fulfilled ? items.Count : scope.Count(e => e.WantedQuantity == 0),
            SetAsideLocationId = setAside?.Id,
            SetAsideLocationName = setAside?.Name,
        };
    }

    /// <summary>The owned copies on the Buy List, grouped by printing and finish.</summary>
    private static List<OwnedPrinting> OwnedCopies(OmniCardDbContext ctx, CardGame? game, IReadOnlyCollection<int>? siteIds) =>
        CollectionQueryBuilder.BuildFilteredQuery(ctx, "is:buylist", game, null, null, siteIds: siteIds)
            .Where(c => !c.IsTraded && !c.IsMissing)
            .GroupBy(c => new { c.Game, c.Name, c.GameCardId, c.IsFoil })
            .Select(g => new OwnedPrinting(g.Key.Game, g.Key.Name, g.Key.GameCardId, g.Key.IsFoil, g.Sum(c => c.Quantity)))
            .ToList();

    private sealed record OwnedPrinting(CardGame Game, string Name, string GameCardId, bool IsFoil, int Quantity);

    /// <summary>Adds a card. A card already on the list gets its wanted quantity raised instead (a fulfilled
    /// one comes back with the new quantity).</summary>
    public BuyListEntryDto Add(BuyListEntryCreateRequest request, string? userName)
    {
        if (LocationsController.ParseGame(request.Game) is not { } game)
            throw new BuyListException($"Unknown game '{request.Game}'");
        var name = request.Name.Trim();
        if (name.Length == 0 || request.GameCardId.Trim().Length == 0)
            throw new BuyListException("Pick a card to add.");
        if (name.Length > BuyListEntry.MaxNameLength)
            throw new BuyListException("That card name is too long.");

        var quantity = Math.Clamp(request.WantedQuantity, 1, BuyListEntry.MaxQuantity);
        var priority = ParsePriority(request.Priority);
        var note = CleanNote(request.Note);
        var matchName = BuyListMatcher.BaseName(name);
        var cardId = request.GameCardId.Trim();
        var now = DateTime.UtcNow;

        using var ctx = dbFactory.CreateDbContext();
        var same = ctx.BuyListEntries.Where(e => e.Game == game && e.PinPrinting == request.PinPrinting && e.Foil == request.Foil);
        var existing = request.PinPrinting
            ? same.FirstOrDefault(e => e.GameCardId == cardId)
            : same.FirstOrDefault(e => e.MatchName == matchName);
        if (existing is not null)
        {
            existing.WantedQuantity = Math.Min(existing.WantedQuantity + quantity, BuyListEntry.MaxQuantity);
            if (priority > existing.Priority) existing.Priority = priority;
            existing.Note = note ?? existing.Note;
            existing.UpdatedAt = now;
            ctx.SaveChanges();
            return ToDto(existing) with { Merged = true };
        }

        var entry = new BuyListEntry
        {
            Game = game,
            Name = name,
            MatchName = matchName,
            GameCardId = cardId,
            SetCode = request.SetCode,
            SetName = request.SetName,
            CollectorNumber = request.CollectorNumber,
            ImageUri = request.ImageUri,
            PinPrinting = request.PinPrinting,
            Foil = request.Foil,
            WantedQuantity = quantity,
            Priority = priority,
            Note = note,
            CreatedByName = userName,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.BuyListEntries.Add(entry);
        ctx.SaveChanges();
        return ToDto(entry);
    }

    public BuyListEntryDto? Update(int id, BuyListEntryUpdateRequest request)
    {
        using var ctx = dbFactory.CreateDbContext();
        var entry = ctx.BuyListEntries.FirstOrDefault(e => e.Id == id);
        if (entry is null) return null;
        entry.PinPrinting = request.PinPrinting;
        entry.Foil = request.Foil;
        entry.WantedQuantity = Math.Clamp(request.WantedQuantity, 0, BuyListEntry.MaxQuantity);
        entry.Priority = ParsePriority(request.Priority);
        entry.Note = CleanNote(request.Note);
        entry.UpdatedAt = DateTime.UtcNow;
        ctx.SaveChanges();
        return ToDto(entry);
    }

    public bool Delete(int id)
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.BuyListEntries.Where(e => e.Id == id).ExecuteDelete() > 0;
    }

    /// <summary>Chooses where a game's wanted copies are set aside (null clears it).</summary>
    public void SetSetAside(CardGame game, int? locationId)
    {
        var settings = settingsService.Get();
        if (locationId is int id)
        {
            using var ctx = dbFactory.CreateDbContext();
            var container = ctx.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == id)
                ?? throw new BuyListException("That location doesn't exist.", 404);
            if (!Accepts(container, game))
                throw new BuyListException("That location is for another game.");
            settings.SetAsideLocationIds[game] = id;
        }
        else
            settings.SetAsideLocationIds.Remove(game);
        settingsService.Save(settings);
    }

    /// <summary>The game's set-aside location, or null when none is set or it can't take this game's cards
    /// any more (deleted, or now fixed to another game).</summary>
    public (int Id, string Name)? SetAsideLocation(CardGame game)
    {
        using var ctx = dbFactory.CreateDbContext();
        return SetAside(ctx, game);
    }

    private (int Id, string Name)? SetAside(OmniCardDbContext ctx, CardGame game)
    {
        if (!settingsService.Get().SetAsideLocationIds.TryGetValue(game, out var id)) return null;
        var container = ctx.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == id);
        return container is not null && Accepts(container, game) ? (container.Id, container.Name) : null;
    }

    private static bool Accepts(StorageContainer container, CardGame game) =>
        container.Game is null || container.Game == game;

    /// <summary>Flags the scanned cards that are on the game's Buy List, so review can show them.</summary>
    public IReadOnlyList<ScanBuyListResultDto> Evaluate(CardGame game, IReadOnlyList<ScanTagRuleItem> items)
    {
        var candidates = items.Select(i => new BuyListCandidate(game, i.Name, i.GameCardId, i.IsFoil, 0)).ToList();
        return Allocate(candidates)
            .Select((allocation, i) => allocation.Hit is { } hit ? new ScanBuyListResultDto(items[i].Key, hit) : null)
            .OfType<ScanBuyListResultDto>()
            .ToList();
    }

    /// <summary>Works through <paramref name="candidates"/> in order, claiming the copies each can fill.</summary>
    public IReadOnlyList<BuyListAllocation> Allocate(IReadOnlyList<BuyListCandidate> candidates)
    {
        if (candidates.Count == 0) return [];
        var games = candidates.Select(c => c.Game).Distinct().ToList();
        using var ctx = dbFactory.CreateDbContext();
        var entries = ctx.BuyListEntries.AsNoTracking()
            .Where(e => e.WantedQuantity > 0 && games.Contains(e.Game))
            .AsEnumerable()
            .GroupBy(e => e.Game)
            .ToDictionary(g => g.Key, g => BuyListMatcher.InMatchOrder(g).ToList());
        if (entries.Count == 0) return candidates.Select(_ => BuyListAllocation.None).ToList();

        var setAside = games.ToDictionary(g => g, g => SetAside(ctx, g));
        var remaining = entries.Values.SelectMany(l => l).ToDictionary(e => e.Id, e => e.WantedQuantity);
        var result = new List<BuyListAllocation>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var matches = entries.GetValueOrDefault(candidate.Game)?
                .Where(e => BuyListMatcher.Matches(e, candidate.Name, candidate.GameCardId, candidate.IsFoil))
                .ToList() ?? [];
            if (matches.Count == 0)
            {
                result.Add(BuyListAllocation.None);
                continue;
            }

            var takes = new List<(int EntryId, int Copies)>();
            var open = candidate.Quantity;
            foreach (var entry in matches)
            {
                var take = Math.Min(open, remaining[entry.Id]);
                if (take <= 0) continue;
                remaining[entry.Id] -= take;
                open -= take;
                takes.Add((entry.Id, take));
            }

            var best = matches[0];
            var hit = new BuyListHitDto
            {
                EntryId = best.Id,
                Priority = best.Priority.ToString(),
                WantedQuantity = matches.Sum(e => e.WantedQuantity),
                Note = best.Note,
                SetAsideLocationName = setAside[candidate.Game]?.Name,
            };
            result.Add(new BuyListAllocation(hit, candidate.Quantity - open, takes));
        }
        return result;
    }

    /// <summary>Counts allocated copies off their entries. Best-effort: by now the cards are in the
    /// collection, so a failure here is logged rather than failing the commit.</summary>
    public void Consume(IEnumerable<BuyListAllocation> allocations)
    {
        var copies = allocations.SelectMany(a => a.Takes)
            .GroupBy(t => t.EntryId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Copies));
        if (copies.Count == 0) return;
        try
        {
            lock (_countLock)
            {
                using var ctx = dbFactory.CreateDbContext();
                var ids = copies.Keys.ToList();
                var now = DateTime.UtcNow;
                foreach (var entry in ctx.BuyListEntries.Where(e => ids.Contains(e.Id)))
                {
                    var taken = Math.Min(entry.WantedQuantity, copies[entry.Id]);
                    entry.WantedQuantity -= taken;
                    entry.AcquiredQuantity += taken;
                    entry.UpdatedAt = now;
                }
                ctx.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Counting {Count} cards off the Buy List failed", copies.Values.Sum());
        }
    }

    /// <summary>The cards customers see at the kiosk: every wanted card, most wanted first.</summary>
    public IReadOnlyList<KioskBuyListEntryDto> ForKiosk()
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.BuyListEntries.AsNoTracking()
            .Where(e => e.WantedQuantity > 0)
            .AsEnumerable()
            .OrderByDescending(e => e.Priority).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => new KioskBuyListEntryDto
            {
                Game = DtoMapping.GameId(e.Game),
                GameName = DtoMapping.GameDisplayName(e.Game),
                Name = e.PinPrinting ? e.Name : e.MatchName,
                SetName = e.PinPrinting ? e.SetName : null,
                SetCode = e.PinPrinting ? e.SetCode : null,
                ImageUri = e.ImageUri,
                Foil = e.Foil,
                Priority = e.Priority.ToString(),
            })
            .ToList();
    }

    private static BuyListPriority ParsePriority(string? value) =>
        Enum.TryParse<BuyListPriority>(value, true, out var priority) && Enum.IsDefined(priority) ? priority : BuyListPriority.Normal;

    private static string? CleanNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > BuyListEntry.MaxNoteLength ? trimmed[..BuyListEntry.MaxNoteLength] : trimmed;
    }

    private static BuyListEntryDto ToDto(BuyListEntry e) => new()
    {
        Id = e.Id,
        Game = DtoMapping.GameId(e.Game),
        Name = e.Name,
        GameCardId = e.GameCardId,
        SetCode = e.SetCode,
        SetName = e.SetName,
        CollectorNumber = e.CollectorNumber,
        ImageUri = e.ImageUri,
        PinPrinting = e.PinPrinting,
        Foil = e.Foil,
        WantedQuantity = e.WantedQuantity,
        AcquiredQuantity = e.AcquiredQuantity,
        Priority = e.Priority.ToString(),
        Note = e.Note,
        // SQL Server hands DateTimes back unspecified; mark them UTC so the browser shows local time.
        CreatedAt = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
    };
}
