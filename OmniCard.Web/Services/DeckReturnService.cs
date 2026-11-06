using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Collection.Inventory;
using OmniCard.Collection.Lists;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Storage;
using OmniCard.Shared.Tags;

namespace OmniCard.Web.Services;

/// <summary>Deck deconstruction and "return to where it came from": sends a deck box's copies back to the
/// location (and binder pocket / section) they were in before they went into a deck — the origin
/// <see cref="DeckOrigin"/> records on every move — and writes a put-back report that says where each card
/// goes. Rules:
/// <list type="bullet">
/// <item>No known origin → the fallback location the user chose (Bulk by default).</item>
/// <item>Origin deleted, in a site the user can't edit, or now fixed to another game → Bulk (the default
/// site, always editable), flagged on the report.</item>
/// <item>The origin pocket now holds another card → Bulk, flagged (the card in the pocket stays put). When it
/// holds an identical copy of the same stack, the copies rejoin it instead.</item>
/// <item>A loose return rejoins an identical stack already at the destination (same printing, condition,
/// language, note, cost, date, source and tags, neither one listed, held, or otherwise referenced).</item>
/// </list>
/// The deck's reserved <c>commander</c>/<c>sideboard</c> tags are removed from returned copies. Nothing is
/// changed until <see cref="Apply"/>, which re-plans against the collection as it is then.</summary>
public sealed class DeckReturnService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IListService lists,
    ILogger<DeckReturnService> logger)
{
    private static readonly string[] DeckRoleTags = [DeckCardClassifier.SideboardTag, DeckCardClassifier.CommanderTag];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A request the user can fix. <see cref="NotFound"/>: the deck box doesn't exist.</summary>
    public sealed class DeckReturnException(string message, bool notFound = false) : Exception(message)
    {
        public bool NotFound { get; } = notFound;
    }

    /// <summary>What returning would do; changes nothing.</summary>
    public DeckReturnPreviewDto Preview(int deckBoxId, DeckReturnRequest request, SiteAccess access)
    {
        using var ctx = dbFactory.CreateDbContext();
        var plan = BuildPlan(ctx, deckBoxId, request, access);
        return new DeckReturnPreviewDto(plan.Box.Id, plan.Box.Name, plan.WholeDeck, plan.Fallback.Id, plan.Fallback.Name,
            plan.Steps.Sum(s => s.Quantity), plan.Steps.Select(s => ToDto(ToLine(plan, s, s.Lot.Id))).ToList());
    }

    /// <summary>Returns the copies, saves the put-back report and (whole deck + <see cref="DeckReturnRequest.SaveAsListName"/>)
    /// a card list of what was in the deck.</summary>
    public DeckReturnResultDto Apply(int deckBoxId, DeckReturnRequest request, SiteAccess access, string? userName)
    {
        using var ctx = dbFactory.CreateDbContext();
        var plan = BuildPlan(ctx, deckBoxId, request, access);
        if (plan.Steps.Count == 0)
            throw new DeckReturnException(plan.WholeDeck
                ? $"\"{plan.Box.Name}\" is already empty."
                : "None of those cards are in the deck box any more.");

        var note = $"Returned from deck box \"{plan.Box.Name}\"";
        var lines = new List<DeckReturnLine>();
        using (var tx = ctx.Database.BeginTransaction())
        {
            var roleTagIds = ctx.Tags.Where(t => DeckRoleTags.Contains(t.Name.ToLower())).Select(t => t.Id).ToList();
            var landed = new List<(Step Step, InventoryLot Final)>();
            foreach (var step in plan.Steps)
            {
                var lot = step.Lot;
                var whole = step.Quantity >= lot.Quantity;
                InventoryLot final;
                if (step.MergeInto is { } target)
                {
                    target.Quantity += step.Quantity;
                    if (whole)
                    {
                        ctx.LotTags.RemoveRange(ctx.LotTags.Where(t => t.LotId == lot.Id));
                        ctx.Lots.Remove(lot);
                    }
                    else
                        lot.Quantity -= step.Quantity;
                    final = target;
                }
                else if (whole)
                {
                    lot.LocationId = step.Destination.Id;
                    lot.Page = step.Page;
                    lot.Slot = step.Slot;
                    lot.Section = step.Section;
                    DeckOrigin.Clear(lot);
                    if (roleTagIds.Count > 0)
                        ctx.LotTags.RemoveRange(ctx.LotTags.Where(t => t.LotId == lot.Id && roleTagIds.Contains(t.TagId)));
                    final = lot;
                }
                else
                {
                    lot.Quantity -= step.Quantity;
                    final = new InventoryLot
                    {
                        ProductId = lot.ProductId,
                        Product = lot.Product,
                        Quantity = step.Quantity,
                        UnitCost = lot.UnitCost,
                        AcquisitionDate = lot.AcquisitionDate,
                        Source = lot.Source,
                        Condition = lot.Condition,
                        Language = lot.Language,
                        Note = lot.Note,
                        LocationId = step.Destination.Id,
                        Page = step.Page,
                        Slot = step.Slot,
                        Section = step.Section,
                    };
                    ctx.Lots.Add(final);
                }
                landed.Add((step, final));
            }
            ctx.SaveChanges();

            // Split lots only get their ids on save, so the movement rows are written afterwards.
            foreach (var (step, final) in landed)
            {
                ctx.Movements.Add(new InventoryMovement
                {
                    ProductId = final.ProductId,
                    LotId = final.Id,
                    Type = MovementType.Move,
                    Quantity = step.Quantity,
                    Note = note,
                });
                lines.Add(ToLine(plan, step, final.Id));
            }

            var report = new DeckReturnReport
            {
                DeckBoxId = plan.Box.Id,
                DeckBoxName = Truncate(plan.Box.Name, DeckReturnReport.MaxNameLength)!,
                CreatedBy = Truncate(userName, DeckReturnReport.MaxNameLength),
                WholeDeck = plan.WholeDeck,
                CardCount = lines.Sum(l => l.Quantity),
                LinesJson = JsonSerializer.Serialize(lines, Json),
            };
            ctx.DeckReturnReports.Add(report);
            ctx.SaveChanges();
            tx.Commit();

            var listId = plan.WholeDeck && plan.Box.Game is CardGame game ? SaveAsList(request.SaveAsListName, game, lines) : null;
            logger.LogInformation("Returned {Count} card(s) from deck box {DeckBoxId} (report {ReportId}, whole deck: {WholeDeck})",
                report.CardCount, plan.Box.Id, report.Id, plan.WholeDeck);
            return new DeckReturnResultDto(report.Id, report.CardCount, listId, lines.Select(ToDto).ToList());
        }
    }

    /// <summary>The deck box's saved put-back reports, newest first.</summary>
    public IReadOnlyList<DeckReturnReportSummaryDto> Reports(int deckBoxId)
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.DeckReturnReports.AsNoTracking()
            .Where(r => r.DeckBoxId == deckBoxId)
            .OrderByDescending(r => r.CreatedUtc)
            .Select(r => new DeckReturnReportSummaryDto(r.Id, r.CreatedUtc, r.CreatedBy, r.WholeDeck, r.CardCount))
            .ToList();
    }

    /// <summary>One saved report and its lines, or null when it isn't this deck box's.</summary>
    public (DeckReturnReport Report, IReadOnlyList<DeckReturnLine> Lines)? Report(int deckBoxId, int reportId)
    {
        using var ctx = dbFactory.CreateDbContext();
        var report = ctx.DeckReturnReports.AsNoTracking().FirstOrDefault(r => r.Id == reportId && r.DeckBoxId == deckBoxId);
        if (report is null)
            return null;
        var lines = JsonSerializer.Deserialize<List<DeckReturnLine>>(report.LinesJson, Json) ?? [];
        return (report, lines);
    }

    /// <summary>"Return" cuts of a deck sync: sends those copies back where they came from (and saves a
    /// put-back report). Used by <see cref="DeckBoxSyncService.Apply"/>.</summary>
    public DeckReturnResultDto ReturnCuts(int deckBoxId, IReadOnlyList<DeckSyncPickDto> lots, int? fallbackLocationId,
        SiteAccess access, string? userName) =>
        Apply(deckBoxId, new DeckReturnRequest { Lots = lots, FallbackLocationId = fallbackLocationId }, access, userName);

    /// <summary>Checks a return request without planning it (the deck box exists and is a deck box, the
    /// fallback location is usable) — lets a multi-step write validate before it changes anything.</summary>
    public void Validate(int deckBoxId, int? fallbackLocationId, SiteAccess access)
    {
        using var ctx = dbFactory.CreateDbContext();
        var box = LoadBox(ctx, deckBoxId);
        ResolveFallback(ctx, box, fallbackLocationId, access);
    }

    // --- planning ---

    private sealed class Step
    {
        public required InventoryLot Lot { get; init; }
        public required int Quantity { get; init; }
        public StorageContainer Destination { get; set; } = null!;
        public int? Page { get; set; }
        public int? Slot { get; set; }
        public string? Section { get; set; }
        public InventoryLot? MergeInto { get; set; }
        public DeckReturnIssue Issue { get; set; }
        public string? OriginName { get; set; }
        // Captured at plan time: Apply clears the lot's own origin before the report lines are written.
        public int? OriginPage { get; init; }
        public int? OriginSlot { get; init; }
        public string? OriginSection { get; init; }
        public string? SlotTakenBy { get; set; }
    }

    private sealed record Plan(
        StorageContainer Box, bool WholeDeck, StorageContainer Fallback, IReadOnlyList<Step> Steps,
        IReadOnlyDictionary<int, string> SiteNames);

    private static StorageContainer LoadBox(OmniCardDbContext ctx, int deckBoxId)
    {
        var box = ctx.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == deckBoxId)
            ?? throw new DeckReturnException("That location no longer exists.", notFound: true);
        if (box.ContainerType != ContainerType.DeckBox)
            throw new DeckReturnException("Only a deck box's cards can be returned to where they came from.");
        return box;
    }

    private static StorageContainer ResolveFallback(OmniCardDbContext ctx, StorageContainer box, int? fallbackLocationId, SiteAccess access)
    {
        if (fallbackLocationId is not int id)
            return Bulk(ctx);
        var fallback = ctx.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == id)
            ?? throw new DeckReturnException("The location chosen for cards with no known origin no longer exists.");
        if (fallback.Id == box.Id || fallback.ContainerType == ContainerType.DeckBox)
            throw new DeckReturnException("Choose a location that isn't a deck box for cards with no known origin.");
        if (!access.CanWrite(fallback.SiteId))
            throw new DeckReturnException($"You can't move cards into \"{fallback.Name}\".");
        return fallback;
    }

    private static StorageContainer Bulk(OmniCardDbContext ctx) =>
        ctx.StorageContainers.AsNoTracking().First(c => c.IsSystem);

    private static Plan BuildPlan(OmniCardDbContext ctx, int deckBoxId, DeckReturnRequest request, SiteAccess access)
    {
        var box = LoadBox(ctx, deckBoxId);
        var bulk = Bulk(ctx);
        var fallback = ResolveFallback(ctx, box, request.FallbackLocationId, access);

        var deckLots = ctx.Lots.Include(l => l.Product)
            .Where(l => l.LocationId == box.Id && l.Product.Category == ProductCategory.Single)
            .ToDictionary(l => l.Id);

        var wholeDeck = request.Lots is not { Count: > 0 } && request.LotIds is not { Count: > 0 };
        List<(InventoryLot Lot, int Quantity)> picks;
        if (wholeDeck)
            picks = deckLots.Values.Select(l => (l, Math.Max(l.Quantity, 1))).ToList();
        else
        {
            picks = [];
            // Whole lots count as all their copies; a lot named both ways is returned whole.
            var wholeLots = (request.LotIds ?? []).ToHashSet();
            var requested = (request.Lots ?? []).Where(p => p.Quantity > 0 && !wholeLots.Contains(p.LotId))
                .GroupBy(p => p.LotId).Select(g => (LotId: g.Key, Quantity: (int?)g.Sum(p => p.Quantity)))
                .Concat(wholeLots.Select(id => (LotId: id, Quantity: (int?)null)));
            foreach (var (lotId, asked) in requested)
            {
                if (!deckLots.TryGetValue(lotId, out var lot))
                    throw new DeckReturnException("One of the cards to return is no longer in the deck box. Refresh and try again.");
                var quantity = asked ?? Math.Max(lot.Quantity, 1);
                if (quantity > lot.Quantity)
                    throw new DeckReturnException($"Only {lot.Quantity} cop{(lot.Quantity == 1 ? "y" : "ies")} of {lot.Product.Name} are in the deck box.");
                picks.Add((lot, quantity));
            }
        }

        var listedIds = ctx.Listings.AsNoTracking()
            .Where(l => l.Status == ListingStatus.Listed || l.Status == ListingStatus.Picked)
            .Select(l => l.LotId);
        foreach (var (lot, quantity) in picks)
            if (quantity < lot.Quantity && listedIds.Contains(lot.Id))
                throw new DeckReturnException($"{lot.Product.Name} is listed for sale — unlist it before returning only some of its copies.");

        // Origins, the pockets they point at, and the loose stacks a return could rejoin.
        var originIds = picks.Select(p => p.Lot.OriginLocationId).OfType<int>().Distinct().ToList();
        var containers = ctx.StorageContainers.AsNoTracking()
            .Where(c => originIds.Contains(c.Id) || c.Id == bulk.Id || c.Id == fallback.Id)
            .ToDictionary(c => c.Id);
        var siteNames = ctx.Sites.AsNoTracking().ToDictionary(s => s.Id, s => s.Name);

        var pocketOrigins = picks.Where(p => p.Lot.OriginPage is not null && p.Lot.OriginSlot is not null)
            .Select(p => p.Lot.OriginLocationId).OfType<int>().Distinct().ToList();
        var occupants = ctx.Lots.Include(l => l.Product)
            .Where(l => l.LocationId != null && pocketOrigins.Contains(l.LocationId.Value) && l.Page != null && l.Slot != null)
            .ToList()
            .GroupBy(l => (l.LocationId!.Value, l.Page!.Value, l.Slot!.Value))
            .ToDictionary(g => g.Key, g => g.First());

        var productIds = picks.Select(p => p.Lot.ProductId).Distinct().ToList();
        var destIds = containers.Keys.ToList();
        var looseStacks = ctx.Lots
            .Where(l => l.LocationId != null && destIds.Contains(l.LocationId.Value) && l.Page == null && l.Slot == null
                        && productIds.Contains(l.ProductId))
            .ToList();

        var merge = new MergeRules(ctx, picks.Select(p => p.Lot.Id)
            .Concat(looseStacks.Select(l => l.Id)).Concat(occupants.Values.Select(l => l.Id)).ToList());

        var claimed = new Dictionary<(int, int, int), Step>();
        var steps = new List<Step>();
        foreach (var (lot, quantity) in picks
                     .OrderBy(p => p.Lot.Product.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(p => p.Lot.Id))
        {
            var step = new Step
            {
                Lot = lot, Quantity = quantity,
                OriginPage = lot.OriginPage, OriginSlot = lot.OriginSlot, OriginSection = lot.OriginSection,
            };
            steps.Add(step);
            var game = lot.Product.Game;

            StorageContainer? origin = null;
            if (lot.OriginLocationId is int originId)
            {
                if (!containers.TryGetValue(originId, out origin))
                    step.Issue = DeckReturnIssue.LocationGone;
                else if (origin.ContainerType == ContainerType.DeckBox)
                    origin = null; // never recorded by DeckOrigin; treat as unknown
            }

            if (origin is not null)
            {
                step.OriginName = origin.Name;
                if (!access.CanWrite(origin.SiteId))
                    step.Issue = DeckReturnIssue.NoAccess;
                else if (origin.Game is CardGame originGame && originGame != game)
                    step.Issue = DeckReturnIssue.WrongGame;
            }
            else if (step.Issue == DeckReturnIssue.None)
                step.Issue = DeckReturnIssue.NoOrigin;

            if (step.Issue == DeckReturnIssue.None && origin is not null)
            {
                if (lot.OriginPage is int page && lot.OriginSlot is int slot)
                {
                    var key = (origin.Id, page, slot);
                    if (claimed.TryGetValue(key, out var other))
                    {
                        step.Issue = DeckReturnIssue.SlotTaken;
                        step.SlotTakenBy = other.Lot.Product.Name;
                    }
                    else if (occupants.TryGetValue(key, out var occupant))
                    {
                        if (merge.CanMerge(lot, occupant))
                        {
                            step.MergeInto = occupant;
                            Place(step, origin, page, slot, occupant.Section);
                        }
                        else
                        {
                            step.Issue = DeckReturnIssue.SlotTaken;
                            step.SlotTakenBy = occupant.Product.Name;
                        }
                    }
                    else
                        Place(step, origin, page, slot, lot.OriginSection);

                    if (step.Issue == DeckReturnIssue.None)
                        claimed[key] = step;
                }
                else
                    Place(step, origin, null, null, lot.OriginSection);
            }

            if (step.Issue != DeckReturnIssue.None)
            {
                // No origin → the chosen fallback; any other problem → Bulk. A fallback fixed to another
                // game can't take the card either, so it goes to Bulk too.
                var dest = step.Issue == DeckReturnIssue.NoOrigin ? fallback : bulk;
                if (dest.Game is CardGame destGame && destGame != game)
                    dest = bulk;
                Place(step, dest, null, null, null);
            }

            if (step.MergeInto is null && step.Page is null)
                step.MergeInto = looseStacks.FirstOrDefault(s =>
                    s.LocationId == step.Destination.Id
                    && string.Equals(s.Section ?? "", step.Section ?? "", StringComparison.Ordinal)
                    && merge.CanMerge(lot, s));
        }

        // Walk order: site ▸ location ▸ section ▸ page ▸ slot ▸ card.
        var ordered = steps
            .OrderBy(s => siteNames.GetValueOrDefault(s.Destination.SiteId) ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Destination.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Section ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Page ?? int.MaxValue)
            .ThenBy(s => s.Slot ?? int.MaxValue)
            .ThenBy(s => s.Lot.Product.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new Plan(box, wholeDeck, fallback, ordered, siteNames);
    }

    private static void Place(Step step, StorageContainer dest, int? page, int? slot, string? section)
    {
        step.Destination = dest;
        step.Page = page;
        step.Slot = slot;
        step.Section = section;
    }

    /// <summary>When returned copies may join an existing lot instead of moving as their own: only when the two
    /// are interchangeable and nothing else points at the lot that would disappear.</summary>
    private sealed class MergeRules
    {
        private readonly HashSet<int> _referenced;
        private readonly HashSet<int> _busyTargets;
        private readonly Dictionary<int, HashSet<int>> _tags;
        private readonly HashSet<int> _roleTagIds;

        public MergeRules(OmniCardDbContext ctx, IReadOnlyCollection<int> lotIds)
        {
            var ids = lotIds.Distinct().ToList();
            var active = ctx.Listings.AsNoTracking()
                .Where(l => ids.Contains(l.LotId) && (l.Status == ListingStatus.Listed || l.Status == ListingStatus.Picked))
                .Select(l => l.LotId).ToList();
            var ebay = ctx.EbayListings.AsNoTracking().Where(l => ids.Contains(l.LotId)).Select(l => l.LotId).ToList();
            var holds = ctx.KioskHolds.AsNoTracking().Where(h => ids.Contains(h.LotId)).Select(h => h.LotId).ToList();
            _busyTargets = [.. active, .. ebay, .. holds];

            // Anything that points at the returning lot by id would dangle if it were merged away.
            _referenced =
            [
                .. _busyTargets,
                .. ctx.Listings.AsNoTracking().Where(l => ids.Contains(l.LotId)).Select(l => l.LotId),
                .. ctx.OrderLines.AsNoTracking().Where(l => l.LotId != null && ids.Contains(l.LotId.Value)).Select(l => l.LotId!.Value),
                .. ctx.CardListItems.AsNoTracking().Where(i => i.SourceLotId != null && ids.Contains(i.SourceLotId.Value)).Select(i => i.SourceLotId!.Value),
                .. ctx.FlagResolutions.AsNoTracking().Where(f => ids.Contains(f.LotId)).Select(f => f.LotId),
                .. ctx.IntakeLines.AsNoTracking().Where(l => l.LotId != null && ids.Contains(l.LotId.Value)).Select(l => l.LotId!.Value),
                .. ctx.Trades.AsNoTracking().Where(t => t.OriginalLotId != null && ids.Contains(t.OriginalLotId.Value)).Select(t => t.OriginalLotId!.Value),
            ];

            _roleTagIds = ctx.Tags.AsNoTracking().Where(t => DeckRoleTags.Contains(t.Name.ToLower())).Select(t => t.Id).ToHashSet();
            _tags = ctx.LotTags.AsNoTracking().Where(t => ids.Contains(t.LotId)).ToList()
                .GroupBy(t => t.LotId)
                .ToDictionary(g => g.Key, g => g.Select(t => t.TagId).ToHashSet());
        }

        public bool CanMerge(InventoryLot returning, InventoryLot target) =>
            returning.Id != target.Id
            && returning.ProductId == target.ProductId
            && returning.Condition == target.Condition
            && returning.Language == target.Language
            && returning.Note == target.Note
            && returning.UnitCost == target.UnitCost
            && returning.AcquisitionDate == target.AcquisitionDate
            && returning.Source == target.Source
            && returning.ScanImagePath is null
            && !returning.IsMissing && !target.IsMissing
            && !returning.IsTraded && !target.IsTraded
            && returning.FlagReason is null && target.FlagReason is null
            && !_referenced.Contains(returning.Id)
            && !_busyTargets.Contains(target.Id)
            && TagsOf(returning).SetEquals(TagsOf(target));

        // The deck's role tags (sideboard/commander) are dropped on return, so they don't count.
        private HashSet<int> TagsOf(InventoryLot lot) =>
            _tags.TryGetValue(lot.Id, out var tags) ? tags.Where(t => !_roleTagIds.Contains(t)).ToHashSet() : [];
    }

    // --- report lines ---

    private static DeckReturnLine ToLine(Plan plan, Step step, int finalLotId)
    {
        var lot = step.Lot;
        var p = lot.Product;
        return new DeckReturnLine
        {
            LotId = finalLotId,
            CardName = p.Name,
            SetCode = p.SetCode,
            CollectorNumber = p.CollectorNumber,
            IsFoil = p.Foil,
            Condition = lot.Condition,
            Language = lot.Language,
            Quantity = step.Quantity,
            DestinationId = step.Destination.Id,
            DestinationName = step.Destination.Name,
            DestinationType = step.Destination.ContainerType.ToString(),
            SiteName = plan.SiteNames.GetValueOrDefault(step.Destination.SiteId),
            Page = step.Page,
            Slot = step.Slot,
            Section = step.Section,
            RejoinedStack = step.MergeInto is not null,
            Issue = step.Issue,
            OriginName = step.Issue == DeckReturnIssue.None ? null : step.OriginName,
            OriginPage = step.Issue == DeckReturnIssue.None ? null : step.OriginPage,
            OriginSlot = step.Issue == DeckReturnIssue.None ? null : step.OriginSlot,
            OriginSection = step.Issue == DeckReturnIssue.None ? null : step.OriginSection,
            SlotTakenBy = step.SlotTakenBy,
        };
    }

    public static DeckReturnLineDto ToDto(DeckReturnLine l) => new(
        l.LotId, l.CardName, l.SetCode, l.CollectorNumber, l.IsFoil, l.Condition, l.Language, l.Quantity,
        l.DestinationId, l.DestinationName, l.DestinationType, l.SiteName, l.Page, l.Slot, l.Section, l.RejoinedStack,
        l.Issue.ToString(), l.OriginName, l.OriginPage, l.OriginSlot, l.OriginSection, l.SlotTakenBy);

    private int? SaveAsList(string? name, CardGame game, IReadOnlyList<DeckReturnLine> lines)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        try
        {
            var list = lists.CreateList(trimmed, game);
            foreach (var line in lines)
                lists.AddOwnedLot(list.Id, line.LotId, line.Quantity);
            return list.Id;
        }
        catch (Exception ex)
        {
            // The cards are already back; a failed list is reported in the log, not as a failed return.
            logger.LogWarning(ex, "Couldn't save the deconstructed deck as list \"{Name}\"", trimmed);
            return null;
        }
    }

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
