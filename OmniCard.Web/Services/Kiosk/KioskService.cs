using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Inventory;
using OmniCard.Shared.Kiosk;
using OmniCard.Shared.Sales;
using OmniCard.Web.Api.Mapping;

namespace OmniCard.Web.Services.Kiosk;

/// <summary>A kiosk request that can't be honored; the controllers turn it into <see cref="StatusCode"/>
/// with <c>{ error, code }</c> so the kiosk can react to <see cref="Code"/>.</summary>
public sealed class KioskException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

/// <summary>Fills art and (optionally) market prices on cards for display; the web implementation is
/// <see cref="KioskCardEnricher"/>.</summary>
public interface IKioskCardEnricher
{
    void Enrich(IReadOnlyList<CollectionCard> cards, bool prices);
}

/// <summary>
/// The customer kiosk: enrolled devices, customer sessions, the copies they hold, and the staff queue that
/// turns a submitted pick list into an order.
///
/// <para><b>Holds.</b> A customer adds a card <i>group</i> — one printing in one condition + language across
/// the kiosk's locations — and the service picks the lots. A lot's available copies are its quantity minus
/// copies held by other open sessions, copies on open (Created/Packed) orders and copies listed on an
/// external channel (TCGplayer/eBay). Every write runs under one process-wide lock, so two kiosks can never
/// hold the same copy (the app runs as a single server process).</para>
///
/// <para><b>Expiry.</b> A Browsing session idle past <see cref="KioskSettings.IdleTimeoutMinutes"/> expires:
/// it is closed lazily by the next write or queue read, and its holds stop counting at once (availability
/// ignores idle Browsing sessions even before they're closed). Queued/Picking lists never expire.</para>
/// </summary>
public sealed class KioskService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IKioskSettingsService settingsService,
    IKioskCardEnricher enricher,
    TimeProvider time)
{
    private readonly object _sync = new();

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // ── Devices ────────────────────────────────────────────────────────────────────────────────

    public KioskDevice Enroll(string? name, string createdBy)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) trimmed = "Kiosk";
        if (trimmed.Length > KioskDevice.MaxNameLength) trimmed = trimmed[..KioskDevice.MaxNameLength];

        using var ctx = dbFactory.CreateDbContext();
        var device = new KioskDevice { Name = trimmed, CreatedAt = Now, CreatedBy = createdBy, LastSeenAt = Now };
        ctx.KioskDevices.Add(device);
        ctx.SaveChanges();
        return device;
    }

    /// <summary>The device when it exists and isn't revoked (null otherwise); records it as seen.</summary>
    public KioskDevice? GetActiveDevice(int deviceId)
    {
        using var ctx = dbFactory.CreateDbContext();
        var device = ctx.KioskDevices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null || device.RevokedAt is not null) return null;
        // A kiosk polls; a minute's resolution on "last seen" is plenty and spares a write per request.
        if (device.LastSeenAt is null || Now - device.LastSeenAt.Value > TimeSpan.FromMinutes(1))
        {
            device.LastSeenAt = Now;
            ctx.SaveChanges();
        }
        return device;
    }

    public List<KioskDevice> ListDevices()
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.KioskDevices.AsNoTracking().OrderBy(d => d.RevokedAt != null).ThenBy(d => d.Name).ToList();
    }

    /// <summary>Locks the device out and releases its customer's browsing session. False when unknown.</summary>
    public bool Revoke(int deviceId)
    {
        lock (_sync)
        {
            using var ctx = dbFactory.CreateDbContext();
            var device = ctx.KioskDevices.FirstOrDefault(d => d.Id == deviceId);
            if (device is null) return false;
            device.RevokedAt ??= Now;
            foreach (var s in ctx.KioskSessions.Where(s => s.DeviceId == deviceId && s.Status == KioskSessionStatus.Browsing).ToList())
                Close(ctx, s, KioskSessionStatus.Cancelled);
            ctx.SaveChanges();
            return true;
        }
    }

    /// <summary>Removes a revoked device's row for good (its closed sessions stay as history).</summary>
    public bool Delete(int deviceId)
    {
        lock (_sync)
        {
            using var ctx = dbFactory.CreateDbContext();
            var device = ctx.KioskDevices.FirstOrDefault(d => d.Id == deviceId);
            if (device is null) return false;
            if (device.RevokedAt is null)
                throw new KioskException(409, "not-revoked", "Revoke the kiosk before deleting it.");
            ctx.KioskDevices.Remove(device);
            ctx.SaveChanges();
            return true;
        }
    }

    // ── Kiosk (customer side; always scoped to the calling device) ─────────────────────────────

    public KioskStatusDto GetStatus(KioskDevice device)
    {
        var settings = settingsService.Get();
        return new KioskStatusDto
        {
            Enrolled = true,
            DeviceName = device.Name,
            Enabled = settings.Enabled,
            HasLocations = settings.LocationIds.Count > 0,
            ShowPrices = settings.ShowPrices,
            IdleTimeoutMinutes = settings.IdleTimeoutMinutes,
            WelcomeMessage = settings.WelcomeMessage,
            Session = GetSession(device.Id),
        };
    }

    /// <summary>The customer browsing on this device, or null (none, or idle past the timeout).</summary>
    public KioskSessionDto? GetSession(int deviceId)
    {
        var settings = settingsService.Get();
        using var ctx = dbFactory.CreateDbContext();
        var session = CurrentSession(ctx, deviceId, settings);
        return session is null ? null : ToSessionDto(ctx, session, settings);
    }

    public KioskSessionDto StartSession(int deviceId, string? name)
    {
        var trimmed = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (trimmed.Length == 0)
            throw new KioskException(400, "name-required", "Please enter a name.");
        if (trimmed.Length > KioskSession.MaxNameLength)
            throw new KioskException(400, "name-too-long", $"Names can be at most {KioskSession.MaxNameLength} characters.");

        var settings = settingsService.Get();
        if (!settings.Enabled)
            throw new KioskException(409, "closed", "The kiosk is closed.");

        lock (_sync)
        {
            using var ctx = dbFactory.CreateDbContext();
            ExpireIdle(ctx, settings);

            // One customer per device: starting over closes whatever this device had open.
            foreach (var old in ctx.KioskSessions.Where(s => s.DeviceId == deviceId && s.Status == KioskSessionStatus.Browsing).ToList())
                Close(ctx, old, KioskSessionStatus.Cancelled);
            ctx.SaveChanges();

            var key = KioskSession.NameKey(trimmed);
            if (ctx.KioskSessions.Any(s => s.ActiveNameKey == key))
                throw new KioskException(409, "name-taken", $"\"{trimmed}\" is already in use. Please pick another name.");

            var session = new KioskSession
            {
                CustomerName = trimmed,
                ActiveNameKey = key,
                DeviceId = deviceId,
                Status = KioskSessionStatus.Browsing,
                CreatedAt = Now,
                LastActivityAt = Now,
            };
            ctx.KioskSessions.Add(session);
            try
            {
                ctx.SaveChanges();
            }
            catch (DbUpdateException)
            {
                // The unique index is the backstop for a name taken outside this process.
                throw new KioskException(409, "name-taken", $"\"{trimmed}\" is already in use. Please pick another name.");
            }
            return ToSessionDto(ctx, session, settings);
        }
    }

    /// <summary>The customer walked away or pressed "Start over": release everything they held.</summary>
    public void EndSession(int deviceId)
    {
        lock (_sync)
        {
            using var ctx = dbFactory.CreateDbContext();
            foreach (var s in ctx.KioskSessions.Where(s => s.DeviceId == deviceId && s.Status == KioskSessionStatus.Browsing).ToList())
                Close(ctx, s, KioskSessionStatus.Cancelled);
            ctx.SaveChanges();
        }
    }

    /// <summary>Records customer activity so the session doesn't expire while they browse.</summary>
    public KioskSessionDto? Touch(int deviceId)
    {
        var settings = settingsService.Get();
        lock (_sync)
        {
            using var ctx = dbFactory.CreateDbContext();
            var session = CurrentSession(ctx, deviceId, settings, tracked: true);
            if (session is null) return null;
            session.LastActivityAt = Now;
            ctx.SaveChanges();
            return ToSessionDto(ctx, session, settings);
        }
    }

    /// <summary>The games that have cards in the kiosk's locations, with how many copies each.</summary>
    public List<KioskGameDto> GetGames()
    {
        var settings = settingsService.Get();
        if (settings.LocationIds.Count == 0) return [];
        using var ctx = dbFactory.CreateDbContext();
        return ExposedLots(ctx, settings.LocationIds)
            .GroupBy(r => r.Game)
            .Select(g => new { Game = g.Key, Count = g.Sum(r => r.Quantity) })
            .ToList()
            .OrderBy(g => DtoMapping.GameDisplayName(g.Game))
            .Select(g => new KioskGameDto(DtoMapping.GameId(g.Game), DtoMapping.GameDisplayName(g.Game), g.Count))
            .ToList();
    }

    /// <summary>
    /// Browsable cards of one game, grouped by printing + condition + language. <paramref name="q"/> is
    /// plain words, each matching the name, set code or set name (or exactly the collector number).
    /// <paramref name="sort"/> is <c>name</c> (default), <c>price-desc</c> or <c>price-asc</c>. Groups
    /// with nothing left to add are dropped unless this device's customer holds some of them.
    /// </summary>
    public PagedResult<KioskCardDto> Search(int deviceId, CardGame game, string? q, string? sort, int skip, int take)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);
        var settings = settingsService.Get();
        if (settings.LocationIds.Count == 0) return new PagedResult<KioskCardDto>(0, skip, take, []);

        using var ctx = dbFactory.CreateDbContext();
        var query = ExposedLots(ctx, settings.LocationIds).Where(r => r.Game == game);
        foreach (var term in (q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pattern = $"%{EscapeLike(term)}%";
            query = query.Where(r => EF.Functions.Like(r.Name, pattern, "\\")
                || EF.Functions.Like(r.SetCode, pattern, "\\")
                || EF.Functions.Like(r.SetName, pattern, "\\")
                || r.Number == term);
        }
        var rows = query.ToList();

        var session = CurrentSession(ctx, deviceId, settings);
        var mine = session is null ? new Dictionary<int, int>() : HeldBySession(ctx, session.Id);
        var used = UnavailableCopies(ctx, settings);

        var groups = rows
            .GroupBy(r => GroupKey(r.ProductId, r.Condition, r.Language))
            .Select(g => new CardGroup(g.Key, g.ToList(),
                g.Sum(r => Math.Max(0, r.Quantity - used.GetValueOrDefault(r.LotId))),
                g.Sum(r => mine.GetValueOrDefault(r.LotId))))
            .Where(g => g.Available > 0 || g.InMyList > 0)
            .ToList();

        var byPrice = sort is "price-desc" or "price-asc";
        if (byPrice) Enrich(groups, prices: true);

        IOrderedEnumerable<CardGroup> ordered = sort switch
        {
            "price-desc" => groups.OrderByDescending(g => g.Card.MarketPrice).ThenBy(g => g.Card.Name),
            "price-asc" => groups.OrderBy(g => g.Card.MarketPrice).ThenBy(g => g.Card.Name),
            _ => groups.OrderBy(g => g.Card.Name).ThenBy(g => g.Card.SetName).ThenBy(g => g.Card.Number, NaturalNumberComparer.Instance),
        };
        var page = ordered.ThenBy(g => g.Card.Condition).Skip(skip).Take(take).ToList();
        Enrich(page, prices: !byPrice && settings.ShowPrices, art: true);

        var items = page.Select(g => new KioskCardDto
        {
            Key = g.Key,
            Game = DtoMapping.GameId(g.Card.Game),
            Name = g.Card.Name,
            SetName = g.Card.SetName,
            SetCode = g.Card.SetCode,
            Number = g.Card.Number,
            Rarity = g.Card.Rarity,
            Condition = g.Card.Condition,
            Language = g.Card.Language,
            IsFoil = g.Card.IsFoil,
            FoilType = g.Card.FoilType,
            ImageUri = g.Card.ImageUri,
            Price = settings.ShowPrices && g.Card.MarketPrice > 0 ? g.Card.MarketPrice : null,
            Available = g.Available,
            InMyList = g.InMyList,
        }).ToList();
        return new PagedResult<KioskCardDto>(groups.Count, skip, take, items);
    }

    /// <summary>Holds <paramref name="quantity"/> more copies of a card group for this device's customer.</summary>
    public KioskSessionDto AddHold(int deviceId, string key, int quantity)
    {
        if (quantity < 1) throw new KioskException(400, "bad-quantity", "Quantity must be at least 1.");
        var (productId, condition, language) = ParseKey(key);
        var settings = settingsService.Get();

        lock (_sync)
        {
            using var ctx = dbFactory.CreateDbContext();
            ExpireIdle(ctx, settings);
            var session = CurrentSession(ctx, deviceId, settings, tracked: true)
                          ?? throw new KioskException(409, "no-session", "Your session has ended. Please start again.");

            var lots = GroupLots(ctx, settings.LocationIds, productId, condition, language);
            if (lots.Count == 0)
                throw new KioskException(404, "not-found", "That card is no longer available.");

            var used = UnavailableCopies(ctx, settings);
            var free = lots
                .Select(r => (Row: r, Free: Math.Max(0, r.Quantity - used.GetValueOrDefault(r.LotId))))
                .Where(x => x.Free > 0)
                .OrderByDescending(x => x.Free)
                .ToList();
            var available = free.Sum(x => x.Free);
            if (available < quantity)
                throw new KioskException(409, "unavailable", available == 0
                    ? "Sorry, someone else just took the last copy."
                    : $"Only {available} more available.");

            var card = ToCard(lots[0]);
            enricher.Enrich([card], prices: true);
            var price = card.MarketPrice;

            var existing = ctx.KioskHolds.Where(h => h.SessionId == session.Id).ToList();
            var remaining = quantity;
            foreach (var (row, freeCopies) in free)
            {
                if (remaining == 0) break;
                var take = Math.Min(remaining, freeCopies);
                var hold = existing.FirstOrDefault(h => h.LotId == row.LotId);
                if (hold is null)
                    ctx.KioskHolds.Add(new KioskHold { SessionId = session.Id, LotId = row.LotId, Quantity = take, UnitPrice = price, CreatedAt = Now });
                else
                    hold.Quantity += take;
                remaining -= take;
            }

            session.LastActivityAt = Now;
            ctx.SaveChanges();
            return ToSessionDto(ctx, session, settings);
        }
    }

    /// <summary>Releases up to <paramref name="quantity"/> held copies of a card group (newest holds first).</summary>
    public KioskSessionDto RemoveHold(int deviceId, string key, int quantity)
    {
        if (quantity < 1) throw new KioskException(400, "bad-quantity", "Quantity must be at least 1.");
        var (productId, condition, language) = ParseKey(key);
        var settings = settingsService.Get();

        lock (_sync)
        {
            using var ctx = dbFactory.CreateDbContext();
            var session = CurrentSession(ctx, deviceId, settings, tracked: true)
                          ?? throw new KioskException(409, "no-session", "Your session has ended. Please start again.");

            var holds = (from h in ctx.KioskHolds
                         where h.SessionId == session.Id
                         join l in ctx.Lots on h.LotId equals l.Id
                         where l.ProductId == productId
                               && (l.Condition ?? DefaultCondition) == condition
                               && (l.Language ?? CardLanguages.English) == language
                         orderby h.CreatedAt descending, h.Id descending
                         select h).ToList();

            var remaining = quantity;
            foreach (var hold in holds)
            {
                if (remaining == 0) break;
                var drop = Math.Min(remaining, hold.Quantity);
                hold.Quantity -= drop;
                if (hold.Quantity == 0) ctx.KioskHolds.Remove(hold);
                remaining -= drop;
            }

            session.LastActivityAt = Now;
            ctx.SaveChanges();
            return ToSessionDto(ctx, session, settings);
        }
    }

    /// <summary>Sends the customer's pick list to the counter. The holds stay until staff finish it.</summary>
    public KioskSubmitResultDto Submit(int deviceId)
    {
        var settings = settingsService.Get();
        lock (_sync)
        {
            using var ctx = dbFactory.CreateDbContext();
            var session = CurrentSession(ctx, deviceId, settings, tracked: true)
                          ?? throw new KioskException(409, "no-session", "Your session has ended. Please start again.");
            var count = ctx.KioskHolds.Where(h => h.SessionId == session.Id).Sum(h => (int?)h.Quantity) ?? 0;
            if (count == 0)
                throw new KioskException(400, "empty", "Your pick list is empty.");

            session.Status = KioskSessionStatus.Queued;
            session.SubmittedAt = Now;
            session.LastActivityAt = Now;
            ctx.SaveChanges();
            return new KioskSubmitResultDto(session.Id, session.CustomerName, count);
        }
    }

    // ── Staff queue ────────────────────────────────────────────────────────────────────────────

    /// <summary>Every open pick list: submitted ones first (oldest first), then customers still browsing.</summary>
    public List<KioskQueueEntryDto> GetQueue()
    {
        var settings = settingsService.Get();
        lock (_sync)
        {
            using var ctx = dbFactory.CreateDbContext();
            ExpireIdle(ctx, settings);
        }

        using var read = dbFactory.CreateDbContext();
        var sessions = read.KioskSessions.AsNoTracking()
            .Where(s => s.Status == KioskSessionStatus.Browsing || s.Status == KioskSessionStatus.Queued || s.Status == KioskSessionStatus.Picking)
            .ToList()
            .OrderBy(s => s.Status == KioskSessionStatus.Browsing)
            .ThenBy(s => s.SubmittedAt ?? s.CreatedAt)
            .ToList();
        if (sessions.Count == 0) return [];

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var deviceNames = read.KioskDevices.AsNoTracking().ToDictionary(d => d.Id, d => d.Name);
        var lines = (from h in read.KioskHolds.AsNoTracking()
                     where sessionIds.Contains(h.SessionId)
                     join l in read.Lots.AsNoTracking() on h.LotId equals l.Id
                     join p in read.Products.AsNoTracking() on l.ProductId equals p.Id
                     join sc in read.StorageContainers.AsNoTracking() on l.LocationId equals sc.Id into scj
                     from sc in scj.DefaultIfEmpty()
                     select new { Hold = h, Lot = l, Product = p, LocationName = sc == null ? null : sc.Name })
                    .ToList();

        var cards = lines.Select(x => new CollectionCard
        {
            Id = x.Lot.Id,
            Game = x.Product.Game,
            GameCardId = x.Product.GameCardId ?? "",
            Name = x.Product.Name,
            ImageUri = x.Product.ImageUri,
            IsFoil = x.Product.Foil,
        }).ToList();
        enricher.Enrich(cards, prices: false);
        var imageByLot = cards.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().ImageUri);

        return sessions.Select(s =>
        {
            var mine = lines.Where(x => x.Hold.SessionId == s.Id)
                .OrderBy(x => x.LocationName).ThenBy(x => x.Lot.Page).ThenBy(x => x.Lot.Slot).ThenBy(x => x.Product.Name)
                .Select(x => new KioskQueueLineDto
                {
                    HoldId = x.Hold.Id,
                    LotId = x.Lot.Id,
                    Name = x.Product.Name,
                    SetName = x.Product.SetName ?? "",
                    SetCode = x.Product.SetCode ?? "",
                    Number = x.Product.CollectorNumber ?? "",
                    Condition = x.Lot.Condition ?? DefaultCondition,
                    Language = x.Lot.Language ?? CardLanguages.English,
                    IsFoil = x.Product.Foil,
                    ImageUri = imageByLot.GetValueOrDefault(x.Lot.Id),
                    Quantity = x.Hold.Quantity,
                    UnitPrice = x.Hold.UnitPrice,
                    LocationId = x.Lot.LocationId,
                    LocationName = x.LocationName,
                    Page = x.Lot.Page,
                    Slot = x.Lot.Slot,
                    Section = x.Lot.Section,
                })
                .ToList();
            return new KioskQueueEntryDto
            {
                Id = s.Id,
                CustomerName = s.CustomerName,
                Status = s.Status.ToString(),
                DeviceName = deviceNames.GetValueOrDefault(s.DeviceId),
                CreatedAt = s.CreatedAt,
                SubmittedAt = s.SubmittedAt,
                LastActivityAt = s.LastActivityAt,
                ClaimedByName = s.ClaimedByName,
                Lines = mine,
                TotalQuantity = mine.Sum(l => l.Quantity),
                TotalPrice = mine.Sum(l => l.Quantity * l.UnitPrice),
            };
        }).ToList();
    }

    /// <summary>Submitted lists nobody has claimed yet, and customers still browsing (for the nav badge).</summary>
    public KioskQueueCountDto GetQueueCount()
    {
        var cutoff = Now.AddMinutes(-settingsService.Get().IdleTimeoutMinutes);
        using var ctx = dbFactory.CreateDbContext();
        var queued = ctx.KioskSessions.Count(s => s.Status == KioskSessionStatus.Queued);
        var browsing = ctx.KioskSessions.Count(s => s.Status == KioskSessionStatus.Browsing && s.LastActivityAt >= cutoff);
        return new KioskQueueCountDto(queued, browsing);
    }

    /// <summary>A staff member starts pulling the cards (taking over from anyone who claimed it before).</summary>
    public void Claim(int sessionId, int userId, string userName) => Mutate(sessionId, (_, s) =>
    {
        RequireStatus(s, KioskSessionStatus.Queued, KioskSessionStatus.Picking);
        s.Status = KioskSessionStatus.Picking;
        s.ClaimedByUserId = userId;
        s.ClaimedByName = userName;
    });

    /// <summary>Puts a claimed list back in the queue.</summary>
    public void Unclaim(int sessionId) => Mutate(sessionId, (_, s) =>
    {
        RequireStatus(s, KioskSessionStatus.Queued, KioskSessionStatus.Picking);
        s.Status = KioskSessionStatus.Queued;
        s.ClaimedByUserId = null;
        s.ClaimedByName = null;
    });

    /// <summary>Drops <paramref name="quantity"/> copies (all when null) of one held lot — the customer
    /// changed their mind, or the card couldn't be found.</summary>
    public void RemoveQueueHold(int sessionId, int holdId, int? quantity) => Mutate(sessionId, (ctx, s) =>
    {
        RequireStatus(s, KioskSessionStatus.Queued, KioskSessionStatus.Picking);
        var hold = ctx.KioskHolds.FirstOrDefault(h => h.Id == holdId && h.SessionId == sessionId)
                   ?? throw new KioskException(404, "not-found", "That card isn't on this pick list.");
        var drop = quantity is int q ? Math.Clamp(q, 1, hold.Quantity) : hold.Quantity;
        hold.Quantity -= drop;
        if (hold.Quantity == 0) ctx.KioskHolds.Remove(hold);
    });

    /// <summary>Cancels an open list and releases its holds.</summary>
    public void Cancel(int sessionId) => Mutate(sessionId, (ctx, s) =>
    {
        if (!s.IsOpen) throw new KioskException(409, "closed", "This pick list is already closed.");
        Close(ctx, s, KioskSessionStatus.Cancelled);
    });

    /// <summary>
    /// Turns a submitted pick list into a Manual order (status Created) for an existing customer or a new
    /// one, one line per held lot at the price the customer saw. The order's open lines keep the copies
    /// reserved, so the holds are released and the list is closed as Completed. Fails (nothing written)
    /// when a held lot no longer has the copies — e.g. it was sold elsewhere meanwhile.
    /// </summary>
    public KioskCreateOrderResultDto CreateOrder(int sessionId, int? customerId, string? customerName)
    {
        lock (_sync)
        {
            using var ctx = dbFactory.CreateDbContext();
            var session = ctx.KioskSessions.FirstOrDefault(s => s.Id == sessionId)
                          ?? throw new KioskException(404, "not-found", "Pick list not found.");
            RequireStatus(session, KioskSessionStatus.Queued, KioskSessionStatus.Picking);

            var holds = ctx.KioskHolds.Where(h => h.SessionId == sessionId).ToList();
            if (holds.Count == 0)
                throw new KioskException(400, "empty", "This pick list has no cards left.");

            var lotIds = holds.Select(h => h.LotId).ToList();
            var lots = ctx.Lots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionary(l => l.Id);
            var productIds = lots.Values.Select(l => l.ProductId).Distinct().ToList();
            var products = ctx.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionary(p => p.Id);
            var committed = CommittedCopies(ctx);
            var outOfStock = holds
                .Where(h => !lots.TryGetValue(h.LotId, out var lot) || lot.Quantity - committed.GetValueOrDefault(h.LotId) < h.Quantity)
                .Select(h => lots.TryGetValue(h.LotId, out var lot) && products.TryGetValue(lot.ProductId, out var p) ? p.Name : $"lot {h.LotId}")
                .ToList();
            if (outOfStock.Count > 0)
                throw new KioskException(409, "unavailable",
                    $"No longer in stock: {string.Join(", ", outOfStock)}. Remove those cards from the list first.");

            using var tx = ctx.Database.BeginTransaction();

            Customer customer;
            if (customerId is int cid)
            {
                customer = ctx.Customers.FirstOrDefault(c => c.Id == cid)
                           ?? throw new KioskException(404, "customer-not-found", "Customer not found.");
            }
            else
            {
                var name = string.IsNullOrWhiteSpace(customerName) ? session.CustomerName : customerName.Trim();
                customer = new Customer { Name = name, Notes = "Created from the customer kiosk.", CreatedAt = Now };
                ctx.Customers.Add(customer);
                ctx.SaveChanges();
            }

            var order = new Order
            {
                CustomerId = customer.Id,
                Channel = SalesChannel.Manual,
                Status = OrderStatus.Created,
                StageKey = OrderStatus.Created.ToString().ToLowerInvariant(),
                OrderDate = Now,
                CreatedAt = Now,
                Notes = $"Kiosk pick list #{session.Id} ({session.CustomerName})",
            };
            ctx.Orders.Add(order);
            ctx.SaveChanges();

            foreach (var hold in holds)
            {
                var lot = lots[hold.LotId];
                var product = products.GetValueOrDefault(lot.ProductId);
                ctx.OrderLines.Add(new OrderLine
                {
                    OrderId = order.Id,
                    LotId = lot.Id,
                    ProductId = lot.ProductId,
                    NameSnapshot = product?.Name ?? "",
                    SetSnapshot = product?.SetName,
                    ConditionSnapshot = lot.Condition,
                    IsFoilSnapshot = product?.Foil ?? false,
                    Quantity = hold.Quantity,
                    UnitSalePrice = hold.UnitPrice,
                });
            }

            Close(ctx, session, KioskSessionStatus.Completed);
            session.OrderId = order.Id;
            ctx.SaveChanges();
            tx.Commit();
            return new KioskCreateOrderResultDto(order.Id, customer.Id);
        }
    }

    // ── Availability (shared with the order editor) ───────────────────────────────────────────

    /// <summary>Copies per lot held by open kiosk sessions (idle Browsing sessions excluded), optionally
    /// leaving out one session. The order editor subtracts these so it can't sell a held card.</summary>
    public Dictionary<int, int> HeldCopies(OmniCardDbContext ctx, int? exceptSessionId = null)
    {
        var cutoff = Now.AddMinutes(-settingsService.Get().IdleTimeoutMinutes);
        return (from h in ctx.KioskHolds.AsNoTracking()
                join s in ctx.KioskSessions.AsNoTracking() on h.SessionId equals s.Id
                where (s.Status == KioskSessionStatus.Queued || s.Status == KioskSessionStatus.Picking
                       || (s.Status == KioskSessionStatus.Browsing && s.LastActivityAt >= cutoff))
                      && (exceptSessionId == null || s.Id != exceptSessionId)
                group h by h.LotId into g
                select new { LotId = g.Key, Quantity = g.Sum(h => h.Quantity) })
            .ToDictionary(x => x.LotId, x => x.Quantity);
    }

    // ── Internals ──────────────────────────────────────────────────────────────────────────────

    private const string DefaultCondition = "NM";

    private sealed class LotRow
    {
        public int LotId { get; init; }
        public int ProductId { get; init; }
        public CardGame Game { get; init; }
        public string GameCardId { get; init; } = "";
        public string Name { get; init; } = "";
        public string SetName { get; init; } = "";
        public string SetCode { get; init; } = "";
        public string Number { get; init; } = "";
        public string Rarity { get; init; } = "";
        public string? ImageUri { get; init; }
        public string Condition { get; init; } = "";
        public string Language { get; init; } = "";
        public bool IsFoil { get; init; }
        public string? FoilType { get; init; }
        public int Quantity { get; init; }
    }

    private sealed record CardGroup(string Key, List<LotRow> Lots, int Available, int InMyList)
    {
        public CollectionCard Card { get; } = ToCard(Lots[0]);
    }

    /// <summary>Single-card lots in the kiosk's locations that can be sold (not missing, not traded away).</summary>
    private static IQueryable<LotRow> ExposedLots(OmniCardDbContext ctx, IReadOnlyCollection<int> locationIds)
    {
        var ids = locationIds.ToList();
        return from l in ctx.Lots.AsNoTracking()
               where l.LocationId != null && ids.Contains(l.LocationId.Value) && l.Quantity > 0 && !l.IsMissing && !l.IsTraded
               join p in ctx.Products.AsNoTracking() on l.ProductId equals p.Id
               where p.Category == ProductCategory.Single
               select new LotRow
               {
                   LotId = l.Id,
                   ProductId = p.Id,
                   Game = p.Game,
                   GameCardId = p.GameCardId ?? "",
                   Name = p.Name,
                   SetName = p.SetName ?? "",
                   SetCode = p.SetCode ?? "",
                   Number = p.CollectorNumber ?? "",
                   Rarity = p.Rarity ?? "",
                   ImageUri = p.ImageUri,
                   Condition = l.Condition ?? DefaultCondition,
                   Language = l.Language ?? CardLanguages.English,
                   IsFoil = p.Foil,
                   FoilType = p.FoilType,
                   Quantity = l.Quantity,
               };
    }

    private static List<LotRow> GroupLots(OmniCardDbContext ctx, IReadOnlyCollection<int> locationIds, int productId, string condition, string language) =>
        ExposedLots(ctx, locationIds)
            .Where(r => r.ProductId == productId && r.Condition == condition && r.Language == language)
            .ToList();

    private static CollectionCard ToCard(LotRow r) => new()
    {
        Id = r.LotId,
        Game = r.Game,
        GameCardId = r.GameCardId,
        Name = r.Name,
        SetName = r.SetName,
        SetCode = r.SetCode,
        Number = r.Number,
        Rarity = r.Rarity,
        ImageUri = r.ImageUri,
        Condition = r.Condition,
        Language = r.Language,
        IsFoil = r.IsFoil,
        FoilType = r.FoilType,
        Quantity = r.Quantity,
    };

    private void Enrich(IEnumerable<CardGroup> groups, bool prices, bool art = false)
    {
        var cards = groups.Select(g => g.Card).ToList();
        if (cards.Count > 0 && (prices || art)) enricher.Enrich(cards, prices);
    }

    /// <summary>Copies per lot nobody else may take: other customers' holds, open orders, and external listings.</summary>
    private Dictionary<int, int> UnavailableCopies(OmniCardDbContext ctx, KioskSettings settings)
    {
        var result = HeldCopies(ctx);
        foreach (var (lotId, qty) in CommittedCopies(ctx))
            result[lotId] = result.GetValueOrDefault(lotId) + qty;
        foreach (var (lotId, qty) in ExternallyListedCopies(ctx))
            result[lotId] = result.GetValueOrDefault(lotId) + qty;
        return result;
    }

    /// <summary>Copies per lot on an open (Created/Packed) order — they leave inventory when it ships.</summary>
    private static Dictionary<int, int> CommittedCopies(OmniCardDbContext ctx) =>
        (from line in ctx.OrderLines.AsNoTracking()
         join order in ctx.Orders.AsNoTracking() on line.OrderId equals order.Id
         where line.LotId != null && (order.Status == OrderStatus.Created || order.Status == OrderStatus.Packed)
         group line by line.LotId!.Value into g
         select new { LotId = g.Key, Quantity = g.Sum(l => l.Quantity) })
        .ToDictionary(x => x.LotId, x => x.Quantity);

    /// <summary>Copies per lot actively listed on TCGplayer or eBay, which could sell there at any moment.
    /// In-store (Manual) listings stay sellable at the kiosk.</summary>
    private static Dictionary<int, int> ExternallyListedCopies(OmniCardDbContext ctx) =>
        ctx.Listings.AsNoTracking()
            .Where(l => (l.Status == ListingStatus.Listed || l.Status == ListingStatus.Picked) && l.Channel != SalesChannel.Manual)
            .GroupBy(l => l.LotId)
            .Select(g => new { LotId = g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionary(x => x.LotId, x => x.Quantity);

    private static Dictionary<int, int> HeldBySession(OmniCardDbContext ctx, int sessionId) =>
        ctx.KioskHolds.AsNoTracking().Where(h => h.SessionId == sessionId)
            .GroupBy(h => h.LotId)
            .Select(g => new { LotId = g.Key, Quantity = g.Sum(h => h.Quantity) })
            .ToDictionary(x => x.LotId, x => x.Quantity);

    /// <summary>This device's Browsing session, unless it has gone idle past the timeout.</summary>
    private KioskSession? CurrentSession(OmniCardDbContext ctx, int deviceId, KioskSettings settings, bool tracked = false)
    {
        var cutoff = Now.AddMinutes(-settings.IdleTimeoutMinutes);
        var sessions = tracked ? ctx.KioskSessions : ctx.KioskSessions.AsNoTracking();
        return sessions
            .Where(s => s.DeviceId == deviceId && s.Status == KioskSessionStatus.Browsing && s.LastActivityAt >= cutoff)
            .OrderByDescending(s => s.Id)
            .FirstOrDefault();
    }

    /// <summary>Closes Browsing sessions idle past the timeout (call under <see cref="_sync"/>).</summary>
    private void ExpireIdle(OmniCardDbContext ctx, KioskSettings settings)
    {
        var cutoff = Now.AddMinutes(-settings.IdleTimeoutMinutes);
        var idle = ctx.KioskSessions.Where(s => s.Status == KioskSessionStatus.Browsing && s.LastActivityAt < cutoff).ToList();
        if (idle.Count == 0) return;
        foreach (var s in idle) Close(ctx, s, KioskSessionStatus.Expired);
        ctx.SaveChanges();
    }

    private void Close(OmniCardDbContext ctx, KioskSession session, KioskSessionStatus status)
    {
        session.Status = status;
        session.ActiveNameKey = null;
        session.ClosedAt = Now;
        ctx.KioskHolds.RemoveRange(ctx.KioskHolds.Where(h => h.SessionId == session.Id));
    }

    private void Mutate(int sessionId, Action<OmniCardDbContext, KioskSession> change)
    {
        lock (_sync)
        {
            using var ctx = dbFactory.CreateDbContext();
            var session = ctx.KioskSessions.FirstOrDefault(s => s.Id == sessionId)
                          ?? throw new KioskException(404, "not-found", "Pick list not found.");
            change(ctx, session);
            ctx.SaveChanges();
        }
    }

    private static void RequireStatus(KioskSession session, params KioskSessionStatus[] allowed)
    {
        if (!allowed.Contains(session.Status))
            throw new KioskException(409, "wrong-status", session.Status == KioskSessionStatus.Browsing
                ? "The customer hasn't sent this list to the counter yet."
                : "This pick list is already closed.");
    }

    private KioskSessionDto ToSessionDto(OmniCardDbContext ctx, KioskSession session, KioskSettings settings)
    {
        var rows = (from h in ctx.KioskHolds.AsNoTracking()
                    where h.SessionId == session.Id
                    join l in ctx.Lots.AsNoTracking() on h.LotId equals l.Id
                    join p in ctx.Products.AsNoTracking() on l.ProductId equals p.Id
                    select new { Hold = h, Lot = l, Product = p })
                   .ToList();

        var groups = rows
            .GroupBy(x => GroupKey(x.Product.Id, x.Lot.Condition ?? DefaultCondition, x.Lot.Language ?? CardLanguages.English))
            .Select(g =>
            {
                var first = g.OrderBy(x => x.Hold.CreatedAt).First();
                var card = new CollectionCard
                {
                    Id = first.Lot.Id,
                    Game = first.Product.Game,
                    GameCardId = first.Product.GameCardId ?? "",
                    Name = first.Product.Name,
                    ImageUri = first.Product.ImageUri,
                    IsFoil = first.Product.Foil,
                };
                return (Key: g.Key, First: first, Card: card, Quantity: g.Sum(x => x.Hold.Quantity),
                        Total: g.Sum(x => x.Hold.Quantity * x.Hold.UnitPrice));
            })
            .OrderBy(g => g.First.Hold.CreatedAt)
            .ToList();
        if (groups.Count > 0) enricher.Enrich(groups.Select(g => g.Card).ToList(), prices: false);

        var items = groups.Select(g => new KioskSessionItemDto
        {
            Key = g.Key,
            Name = g.First.Product.Name,
            SetName = g.First.Product.SetName ?? "",
            SetCode = g.First.Product.SetCode ?? "",
            Number = g.First.Product.CollectorNumber ?? "",
            Condition = g.First.Lot.Condition ?? DefaultCondition,
            Language = g.First.Lot.Language ?? CardLanguages.English,
            IsFoil = g.First.Product.Foil,
            ImageUri = g.Card.ImageUri,
            Quantity = g.Quantity,
            UnitPrice = settings.ShowPrices ? g.First.Hold.UnitPrice : null,
        }).ToList();

        return new KioskSessionDto
        {
            Id = session.Id,
            CustomerName = session.CustomerName,
            Status = session.Status.ToString(),
            Items = items,
            TotalQuantity = groups.Sum(g => g.Quantity),
            TotalPrice = settings.ShowPrices ? groups.Sum(g => g.Total) : null,
        };
    }

    internal static string GroupKey(int productId, string condition, string language) => $"{productId}|{condition}|{language}";

    private static (int ProductId, string Condition, string Language) ParseKey(string? key)
    {
        var parts = (key ?? "").Split('|');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var productId))
            throw new KioskException(400, "bad-key", "Unknown card.");
        return (productId, parts[1], parts[2]);
    }

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    /// <summary>Orders collector numbers numerically where they are numbers ("2" before "10").</summary>
    private sealed class NaturalNumberComparer : IComparer<string>
    {
        public static readonly NaturalNumberComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (int.TryParse(x, out var a) && int.TryParse(y, out var b)) return a.CompareTo(b);
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }
    }
}
