using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Intake;
using OmniCard.Shared.Storage;
using OmniCard.Web.Api.Controllers;

namespace OmniCard.Web.Services.Intake;

/// <summary>An intake request that can't be done. <see cref="StatusCode"/> is the HTTP status to answer
/// with; <see cref="Quote"/> carries the fresh quote when the prices moved under the staff member (409).</summary>
public sealed class IntakeException(string message, int statusCode = 400, IntakeQuoteDto? quote = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public IntakeQuoteDto? Quote { get; } = quote;
}

/// <summary>
/// Buying singles over the counter. <see cref="Quote"/> prices scanned cards from the catalog (market or
/// low) with the admin's <see cref="IntakeSettings"/>; <see cref="Accept"/> prices them again, records the
/// buy (<see cref="IntakeRecord"/> + lines, for the audit), credits the customer for trade-paid cards and
/// puts the copies into the intake location as lots costed at what was paid. Like a scan commit, the
/// cards carry the tags settled during scan review (rule tags included, minus any the reviewer removed),
/// so tag rules aren't run again here.
/// </summary>
public sealed class IntakeService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IIntakeSettingsService settingsService,
    ICardService cardService,
    ScanCommitService commits,
    StoreCreditService storeCredit,
    ILogger<IntakeService> logger)
{
    private sealed record PricedLine(IntakeQuoteLineRequest Request, CardGame Game, IntakePricingInput Input, IntakePricedLine Priced);

    public IntakeQuoteDto Quote(IntakeQuoteRequest request) =>
        Quote(request.PriceBasis, request.Lines, settingsService.Get()).Dto;

    private (IntakeQuoteDto Dto, List<PricedLine> Lines, IntakeTotals Totals) Quote(
        string priceBasis, IReadOnlyList<IntakeQuoteLineRequest> lines, IntakeSettings settings)
    {
        var basis = ParseBasis(priceBasis);
        var games = new List<CardGame>(lines.Count);
        foreach (var line in lines)
            games.Add(LocationsController.ParseGame(line.Item.Game)
                ?? throw new IntakeException($"Unknown game '{line.Item.Game}'"));

        var (market, low) = LookupPrices(lines, games);
        var priced = new List<PricedLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var key = (games[i], line.Item.GameCardId, line.Item.IsFoil);
            var input = new IntakePricingInput(
                // A card the catalog has no price for stays null (not 0) so it's flagged for a manual price.
                market.TryGetValue(key, out var marketPrice) ? marketPrice : null,
                low.TryGetValue(key, out var lowPrice) ? lowPrice : null,
                line.ManualPrice is { } m ? Math.Round(m, 2) : null,
                line.Item.Condition, Math.Max(1, line.Item.Quantity), line.Excluded, ParsePayout(line.Payout));
            priced.Add(new PricedLine(line, games[i], input, IntakePricing.PriceLine(input, settings, basis)));
        }

        var totals = IntakePricing.Totals(priced.Select(p => (p.Input, p.Priced)).ToList(), settings);
        var dto = new IntakeQuoteDto
        {
            Lines = priced.Select(p => new IntakeQuoteLineDto
            {
                MarketPrice = p.Input.MarketPrice,
                LowPrice = p.Input.LowPrice,
                BasePrice = p.Priced.BasePrice,
                IsManualPrice = p.Priced.IsManualPrice,
                UsedMarketFallback = p.Priced.UsedMarketFallback,
                ConditionPercent = p.Priced.ConditionPercent,
                IsBulk = p.Priced.IsBulk,
                Excluded = p.Priced.Excluded,
                NeedsPrice = p.Priced.NeedsPrice,
                CashUnit = p.Priced.CashUnit,
                TradeUnit = p.Priced.TradeUnit,
            }).ToList(),
            CardCount = totals.CardCount,
            MarketTotal = totals.MarketTotal,
            CashOffer = totals.CashOffer,
            TradeOffer = totals.TradeOffer,
            CashPaid = totals.CashPaid,
            TradePaid = totals.TradePaid,
            CashPercent = settings.CashPercent,
            TradePercent = settings.TradePercent,
            SingleOffer = settings.CashPercent == settings.TradePercent,
            NeedsPrices = priced.Any(p => p.Priced.NeedsPrice),
        };
        return (dto, priced, totals);
    }

    /// <summary>Per-copy market and low prices keyed by (game, card id, foil), one lookup per game+finish.</summary>
    private (Dictionary<(CardGame, string, bool), decimal> Market, Dictionary<(CardGame, string, bool), decimal> Low)
        LookupPrices(IReadOnlyList<IntakeQuoteLineRequest> lines, List<CardGame> games)
    {
        var market = new Dictionary<(CardGame, string, bool), decimal>();
        var low = new Dictionary<(CardGame, string, bool), decimal>();
        var groups = lines.Select((l, i) => (Game: games[i], l.Item.GameCardId, l.Item.IsFoil))
            .Where(k => !string.IsNullOrEmpty(k.GameCardId))
            .GroupBy(k => (k.Game, k.IsFoil));
        foreach (var group in groups)
        {
            var (game, isFoil) = group.Key;
            var ids = group.Select(k => k.GameCardId).Distinct().ToList();
            var service = cardService.GetGameService(game);
            foreach (var (id, price) in service.GetCurrentPrices(ids, isFoil))
                market[(game, id, isFoil)] = price;
            if (service is ILowPriceSource lows)
                foreach (var (id, price) in lows.GetLowPrices(ids, isFoil))
                    low[(game, id, isFoil)] = price;
        }
        return (market, low);
    }

    /// <summary>Buys the included cards. The caller has checked the user may write to
    /// <see cref="IntakeSettings.LocationId"/>.</summary>
    /// <exception cref="IntakeException">Nothing to buy, missing prices/customer/location (400/409), or the
    /// totals no longer match what the seller was shown (409, with the fresh quote).</exception>
    public IntakeAcceptResultDto Accept(IntakeAcceptRequest request, int? userId, string userName)
    {
        var settings = settingsService.Get();
        if (settings.LocationId is not int locationId)
            throw new IntakeException("No intake location is set. An administrator can choose one in Settings ▸ Intake.", 409);

        var (quote, lines, totals) = Quote(request.PriceBasis, request.Lines, settings);
        var bought = lines.Where(l => !l.Priced.Excluded).ToList();
        if (bought.Count == 0)
            throw new IntakeException("There are no cards to buy.");
        if (quote.NeedsPrices)
            throw new IntakeException("Every card needs a price before it can be bought.");
        if (totals.CashPaid != request.ExpectedCashPaid || totals.TradePaid != request.ExpectedTradePaid)
            throw new IntakeException("Prices changed since the quote was shown. Review the new totals.", 409, quote);

        using var db = dbFactory.CreateDbContext();
        var location = db.StorageContainers.AsNoTracking().FirstOrDefault(c => c.Id == locationId)
            ?? throw new IntakeException("The intake location no longer exists. An administrator can choose another in Settings ▸ Intake.", 409);

        string? customerName = null;
        if (request.CustomerId is int customerId)
            customerName = db.Customers.AsNoTracking().Where(c => c.Id == customerId).Select(c => c.Name).FirstOrDefault()
                ?? throw new IntakeException("Customer not found.", 404);
        if (totals.TradePaid > 0 && request.CustomerId is null)
            throw new IntakeException("Choose a customer to receive the store credit.");

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > IntakeRecord.MaxNoteLength }) note = note[..IntakeRecord.MaxNoteLength];

        var record = new IntakeRecord
        {
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            CreatedByName = userName,
            CustomerId = request.CustomerId,
            CustomerName = customerName,
            PriceBasis = ParseBasis(request.PriceBasis),
            CashPercent = settings.CashPercent,
            TradePercent = settings.TradePercent,
            LocationId = locationId,
            LocationName = location.Name,
            CardCount = totals.CardCount,
            MarketTotal = totals.MarketTotal,
            CashPaid = totals.CashPaid,
            TradePaid = totals.TradePaid,
            Note = note,
            SettingsJson = JsonSerializer.Serialize(settings, IntakeSettingsService.JsonOptions),
        };
        var recordLines = bought.Select(l => new IntakeLine
        {
            Game = l.Game,
            GameCardId = l.Request.Item.GameCardId,
            Name = l.Request.Item.Name,
            SetCode = l.Request.Item.SetCode,
            SetName = l.Request.Item.SetName,
            CollectorNumber = l.Request.Item.CollectorNumber,
            IsFoil = l.Request.Item.IsFoil,
            FoilType = l.Request.Item.IsFoil ? l.Request.Item.FoilType : null,
            Condition = string.IsNullOrWhiteSpace(l.Request.Item.Condition) ? "NM" : l.Request.Item.Condition,
            Language = CardLanguages.ToStored(CardLanguages.Normalize(l.Request.Item.Language)),
            Quantity = l.Input.Quantity,
            MarketPrice = l.Input.MarketPrice,
            LowPrice = l.Input.LowPrice,
            BasePrice = l.Priced.BasePrice!.Value,
            IsManualPrice = l.Priced.IsManualPrice,
            ConditionPercent = l.Priced.ConditionPercent,
            IsBulk = l.Priced.IsBulk,
            Payout = l.Input.Payout,
            UnitPaid = l.Priced.PaidUnit(l.Input.Payout),
        }).ToList();

        // The record, its lines and the store credit land together; the ledger lock keeps the credit
        // consistent with any concurrent manual adjustment.
        decimal? balance = storeCredit.Locked(() =>
        {
            using var tx = db.Database.BeginTransaction();
            db.Intakes.Add(record);
            db.SaveChanges();
            foreach (var line in recordLines) line.IntakeId = record.Id;
            db.IntakeLines.AddRange(recordLines);
            if (totals.TradePaid > 0)
                db.StoreCreditEntries.Add(StoreCreditService.NewEntry(request.CustomerId!.Value, totals.TradePaid,
                    $"Intake #{record.Id}", userId, userName, record.Id));
            db.SaveChanges();
            tx.Commit();
            return request.CustomerId is int id ? StoreCreditService.Balance(db, id) : (decimal?)null;
        });

        // Then the cards themselves, costed at what was paid per copy. A failure here undoes the record and
        // its credit so the audit never shows a buy whose cards aren't in inventory.
        IReadOnlyList<int> lotIds;
        try
        {
            var items = bought.Select(l => l.Request.Item with
            {
                Quantity = l.Input.Quantity,
                PurchasePrice = l.Priced.PaidUnit(l.Input.Payout),
                Tags = l.Request.Item.Tags,
            }).ToList();
            lotIds = commits.Commit(locationId, items);
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            db.StoreCreditEntries.Where(e => e.IntakeId == record.Id).ExecuteDelete();
            db.Intakes.Where(i => i.Id == record.Id).ExecuteDelete();
            if (ex is DeckBoxGameMismatchException)
                throw new IntakeException(ex.Message, 409);
            throw;
        }

        try
        {
            for (var i = 0; i < recordLines.Count; i++) recordLines[i].LotId = lotIds[i];
            db.SaveChanges();
            var source = $"Intake #{record.Id}";
            db.Lots.Where(l => lotIds.Contains(l.Id)).ExecuteUpdate(s => s.SetProperty(l => l.Source, source));
        }
        catch (Exception ex)
        {
            // The buy is complete; only its cross-links failed — log rather than fail the request.
            logger.LogWarning(ex, "Intake {IntakeId}: linking lines to their lots failed", record.Id);
        }

        logger.LogInformation("Intake {IntakeId} by {User}: {Cards} card(s), cash {Cash}, trade {Trade}",
            record.Id, userName, totals.CardCount, totals.CashPaid, totals.TradePaid);
        return new IntakeAcceptResultDto(record.Id, totals.CardCount, totals.CashPaid, totals.TradePaid, balance);
    }

    /// <summary>Intake history, newest first, optionally narrowed to one customer or staff member.</summary>
    public IntakePageDto List(int page, int pageSize, int? customerId, int? userId, DateTime? from, DateTime? to)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(0, page);
        using var db = dbFactory.CreateDbContext();
        var query = db.Intakes.AsNoTracking();
        if (customerId is int c) query = query.Where(i => i.CustomerId == c);
        if (userId is int u) query = query.Where(i => i.CreatedByUserId == u);
        if (from is DateTime f) query = query.Where(i => i.CreatedAt >= f);
        if (to is DateTime t) query = query.Where(i => i.CreatedAt < t);
        var total = query.Count();
        var items = query.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
            .Skip(page * pageSize).Take(pageSize).AsEnumerable().Select(ToSummary).ToList();
        return new IntakePageDto(items, total);
    }

    public IntakeDetailDto? Get(int id)
    {
        using var db = dbFactory.CreateDbContext();
        var record = db.Intakes.AsNoTracking().FirstOrDefault(i => i.Id == id);
        if (record is null) return null;
        var lines = db.IntakeLines.AsNoTracking().Where(l => l.IntakeId == id).OrderBy(l => l.Id).ToList();

        IntakeSettings? settings = null;
        try { settings = JsonSerializer.Deserialize<IntakeSettings>(record.SettingsJson, IntakeSettingsService.JsonOptions); }
        catch (JsonException) { }

        return new IntakeDetailDto
        {
            Summary = ToSummary(record),
            CashPercent = record.CashPercent,
            TradePercent = record.TradePercent,
            LocationId = record.LocationId,
            Note = record.Note,
            Settings = settings is null ? null : ToDto(settings, locationName: record.LocationName),
            Lines = lines.Select(l => new IntakeLineDto
            {
                Id = l.Id,
                Game = l.Game.ToString(),
                GameCardId = l.GameCardId,
                Name = l.Name,
                SetCode = l.SetCode,
                SetName = l.SetName,
                CollectorNumber = l.CollectorNumber,
                IsFoil = l.IsFoil,
                FoilType = l.FoilType,
                Condition = l.Condition,
                Language = l.Language,
                Quantity = l.Quantity,
                MarketPrice = l.MarketPrice,
                LowPrice = l.LowPrice,
                BasePrice = l.BasePrice,
                IsManualPrice = l.IsManualPrice,
                ConditionPercent = l.ConditionPercent,
                IsBulk = l.IsBulk,
                Payout = l.Payout.ToString(),
                UnitPaid = l.UnitPaid,
                LotId = l.LotId,
            }).ToList(),
        };
    }

    private static IntakeSummaryDto ToSummary(IntakeRecord r) => new()
    {
        Id = r.Id,
        // SQL Server hands DateTimes back unspecified; mark them UTC so the browser shows local time.
        CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
        CreatedByName = r.CreatedByName,
        CustomerId = r.CustomerId,
        CustomerName = r.CustomerName,
        PriceBasis = r.PriceBasis.ToString(),
        LocationName = r.LocationName,
        CardCount = r.CardCount,
        MarketTotal = r.MarketTotal,
        CashPaid = r.CashPaid,
        TradePaid = r.TradePaid,
    };

    public static IntakeSettingsDto ToDto(IntakeSettings s, string? locationName) => new()
    {
        CashPercent = s.CashPercent,
        TradePercent = s.TradePercent,
        ConditionPercents = s.ConditionPercents,
        BulkThreshold = s.BulkThreshold,
        BulkMode = s.BulkMode.ToString(),
        BulkRate = s.BulkRate,
        RoundingStep = s.RoundingStep,
        RoundingScope = s.RoundingScope.ToString(),
        LocationId = s.LocationId,
        LocationName = locationName,
        CustomerView = new IntakeCustomerViewDto
        {
            ShowCardImages = s.CustomerView.ShowCardImages,
            ShowCardDetails = s.CustomerView.ShowCardDetails,
            ShowCardPrices = s.CustomerView.ShowCardPrices,
            ShowCardOffers = s.CustomerView.ShowCardOffers,
            ShowMarketTotal = s.CustomerView.ShowMarketTotal,
        },
    };

    public static IntakeSettings FromDto(IntakeSettingsDto d) => new()
    {
        CashPercent = d.CashPercent,
        TradePercent = d.TradePercent,
        ConditionPercents = new Dictionary<string, decimal>(d.ConditionPercents, StringComparer.OrdinalIgnoreCase),
        BulkThreshold = d.BulkThreshold,
        BulkMode = Enum.TryParse<IntakeBulkMode>(d.BulkMode, true, out var mode) ? mode : IntakeBulkMode.FlatRate,
        BulkRate = d.BulkRate,
        RoundingStep = d.RoundingStep,
        RoundingScope = Enum.TryParse<IntakeRoundingScope>(d.RoundingScope, true, out var scope) ? scope : IntakeRoundingScope.Total,
        LocationId = d.LocationId,
        CustomerView = new IntakeCustomerView
        {
            ShowCardImages = d.CustomerView.ShowCardImages,
            ShowCardDetails = d.CustomerView.ShowCardDetails,
            ShowCardPrices = d.CustomerView.ShowCardPrices,
            ShowCardOffers = d.CustomerView.ShowCardOffers,
            ShowMarketTotal = d.CustomerView.ShowMarketTotal,
        },
    };

    private static IntakePriceBasis ParseBasis(string? value) =>
        Enum.TryParse<IntakePriceBasis>(value, true, out var basis) ? basis : IntakePriceBasis.Market;

    private static IntakePayout ParsePayout(string? value) =>
        Enum.TryParse<IntakePayout>(value, true, out var payout) ? payout : IntakePayout.Cash;
}
