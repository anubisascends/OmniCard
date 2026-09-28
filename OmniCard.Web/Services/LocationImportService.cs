using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.CardMatching;
using OmniCard.Collection.Lists;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.ImportExport;
using OmniCard.Shared.Lists;
using OmniCard.Shared.Matching;
using OmniCard.Shared.Storage;

namespace OmniCard.Web.Services;

/// <summary>All-or-nothing import of a CSV file or Moxfield/Archidekt deck URL into one location (the
/// Location view's Import). Unlike the lenient Import page there's no location choice and no duplicate
/// skipping: every line is validated first — parsed cleanly, resolved to a real catalog printing,
/// allowed into the location — and if any line fails, nothing is written and every problem is returned
/// so the user can fix them in one pass. The write itself is a single <c>SaveChanges</c>, so it's atomic too.</summary>
public sealed class LocationImportService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    ICsvExportImportService csv,
    WebBinderCardService binderCards,
    IDecklistService decklists,
    ICardService cardService,
    ILogger<LocationImportService> logger)
{
    /// <summary>Exactly one of <see cref="Result"/> / <see cref="Failure"/> is set, unless the location
    /// doesn't exist (<see cref="LocationNotFound"/>).</summary>
    public sealed record Outcome(LocationImportResultDto? Result, LocationImportFailureDto? Failure, bool LocationNotFound = false);

    private sealed record Target(int Id, string Name, ContainerType Type, CardGame? Game)
    {
        /// <summary>A deck box with an assigned game only accepts that game's cards.</summary>
        public CardGame? LockedGame => Type == ContainerType.DeckBox ? Game : null;
    }

    /// <summary>The app's condition codes, plus the spellings people type in hand-edited files.</summary>
    private static readonly Dictionary<string, string> Conditions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NM"] = "NM", ["LP"] = "LP", ["MP"] = "MP", ["HP"] = "HP", ["DMG"] = "DMG", ["D"] = "DMG",
        ["Near Mint"] = "NM", ["Lightly Played"] = "LP", ["Moderately Played"] = "MP",
        ["Heavily Played"] = "HP", ["Damaged"] = "DMG",
    };

    private const string RefreshCatalogHint = "If it's from a new set, refresh the catalog under Settings ▸ Catalog data.";

    public Outcome ImportCsv(int locationId, string filePath, string fileName)
    {
        if (LoadTarget(locationId) is not { } target)
            return new Outcome(null, null, LocationNotFound: true);

        CsvImportPreview preview;
        try
        {
            preview = csv.PreviewImport(filePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Location import: couldn't read {File}", fileName);
            return Fail("Nothing was imported. The file couldn't be read as a CSV — make sure it's saved as comma-separated values with a header row.",
                [new LocationImportIssueDto(null, null, ex.Message)]);
        }

        if (!preview.FormatRecognized)
        {
            var found = preview.Headers.Count > 0 ? string.Join(", ", preview.Headers) : "(none)";
            return Fail("Nothing was imported. The file isn't in a recognized card CSV format.",
            [
                new LocationImportIssueDto(1, null,
                    $"The header row doesn't match a supported format. Found columns: {found}. Supported: an OmniCard export " +
                    "(has a GameCardId column), TCGplayer (Printing column), Moxfield (Edition column), or ManaBox " +
                    "(Foil, Scryfall ID and Purchase price currency columns)."),
            ]);
        }

        if (preview.TotalRows == 0)
            return Fail("Nothing was imported. The file has a header row but no cards below it.", []);

        var errors = preview.Issues.Select(i => new LocationImportIssueDto(i.Row, i.CardName, i.Message)).ToList();
        var substitutions = new List<LocationImportIssueDto>();

        for (var i = 0; i < preview.Cards.Count; i++)
        {
            var card = preview.Cards[i];
            var row = preview.CardRows[i];
            var name = string.IsNullOrWhiteSpace(card.Name) ? null : card.Name.Trim();
            void Error(string message) => errors.Add(new LocationImportIssueDto(row, name, message));

            if (NormalizeCondition(card.Condition) is { } condition)
                card.Condition = condition;
            else
                Error($"Unrecognized condition '{card.Condition}'. Use NM, LP, MP, HP or DMG.");

            if (target.LockedGame is { } lockedGame && card.Game != lockedGame)
            {
                Error($"This is a {GameName(card.Game)} card, but \"{target.Name}\" is a {GameName(lockedGame)} deck box.");
                continue;
            }

            if (GameService(card.Game) is not { } gs)
            {
                Error($"{GameName(card.Game)} cards can't be imported: that game's catalog isn't available.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(card.GameCardId))
            {
                if (!HydrateFromCatalogId(gs, card))
                    Error($"Card ID '{card.GameCardId}' isn't in the {GameName(card.Game)} catalog. Check the ID column, or {LowerFirst(RefreshCatalogHint)}");
                continue;
            }

            if (name is null)
            {
                Error("The card name is blank. Every row needs a card name (or a GameCardId).");
                continue;
            }

            var exact = ResolveCsvPrinting(gs, card);
            var hasPrinting = !string.IsNullOrWhiteSpace(card.SetCode) || !string.IsNullOrWhiteSpace(card.SetName)
                || !string.IsNullOrWhiteSpace(card.Number);
            var printing = exact
                ?? (hasPrinting ? DecklistPrintingResolver.Resolve(gs, new DecklistEntry(card.Quantity, name, null, null)) : null);
            if (printing is null)
            {
                Error($"\"{name}\"{DescribeCsvPrinting(card)} wasn't found in the {GameName(card.Game)} catalog. Check the spelling, set and collector number. {RefreshCatalogHint}");
                continue;
            }

            if (exact is null)
                substitutions.Add(new LocationImportIssueDto(row, name,
                    $"{DescribeCsvPrinting(card).Trim()} isn't in the catalog, so it was imported as {printing.SetCode} #{printing.CollectorNumber}."));
            ApplyPrinting(card, printing);
        }

        if (errors.Count > 0)
        {
            var badRows = errors.Where(e => e.Row is not null).Select(e => e.Row).Distinct().Count();
            return Fail(
                $"Nothing was imported. {Plural(errors.Count, "problem")} found in {Plural(badRows, "row")} of {preview.TotalRows} — fix them and import the file again.",
                errors.OrderBy(e => e.Row ?? 0).ToList());
        }

        foreach (var card in preview.Cards)
            PlaceInTarget(card, target);

        return Write(preview.Cards, target, fileName, FormatName(preview.DetectedFormat), substitutions);
    }

    public async Task<Outcome> ImportUrlAsync(int locationId, LocationUrlImportRequest request)
    {
        if (LoadTarget(locationId) is not { } target)
            return new Outcome(null, null, LocationNotFound: true);

        // Moxfield and Archidekt are MTG-only.
        const CardGame game = CardGame.Mtg;
        var url = request.Url?.Trim() ?? "";
        if (url.Length == 0)
            return Fail("Paste a Moxfield or Archidekt deck URL to import.", []);
        if (DecklistService.ParseUrl(url).Source is null)
            return Fail("Nothing was imported. That isn't a Moxfield or Archidekt deck URL — it should look like https://moxfield.com/decks/… or https://archidekt.com/decks/….", []);
        if (NormalizeCondition(request.Condition ?? "NM") is not { } condition)
            return Fail($"Unrecognized condition '{request.Condition}'. Use NM, LP, MP, HP or DMG.", []);
        if (target.LockedGame is { } lockedGame && lockedGame != game)
            return Fail($"Nothing was imported. \"{target.Name}\" is a {GameName(lockedGame)} deck box, but Moxfield and Archidekt decks are {GameName(game)}.", []);
        if (GameService(game) is not { } gs)
            return Fail($"Nothing was imported. The {GameName(game)} catalog isn't available.", []);

        var fetched = await decklists.FetchDecklistAsync(url);
        if (fetched is null)
            return Fail("Nothing was imported. Couldn't download that deck — check the link, make sure the deck is public (private decks can't be read), and try again.", []);

        var (deckName, entries) = fetched.Value;
        if (entries.Count == 0)
            return Fail($"Nothing was imported. \"{deckName}\" has no cards.", []);

        var errors = new List<LocationImportIssueDto>();
        var substitutions = new List<LocationImportIssueDto>();
        var cards = new Dictionary<(string GameCardId, string? Finish), CollectionCard>();

        foreach (var entry in entries)
        {
            var exact = DecklistPrintingResolver.Resolve(gs, entry);
            var printing = exact ?? DecklistPrintingResolver.Resolve(gs, entry with { SetCode = null, CollectorNumber = null });
            if (printing is null)
            {
                errors.Add(new LocationImportIssueDto(null, entry.CardName,
                    $"Not found in the {GameName(game)} catalog{DescribeDeckPrinting(entry)}. Check the card on the deck site. {RefreshCatalogHint}"));
                continue;
            }
            if (exact is null)
                substitutions.Add(new LocationImportIssueDto(null, entry.CardName,
                    $"{DescribeDeckPrinting(entry).Trim(' ', '(', ')')} isn't in the catalog, so it was imported as {printing.SetCode} #{printing.CollectorNumber}."));

            var key = (printing.GameSpecificId, entry.Finish);
            if (cards.TryGetValue(key, out var existing))
            {
                existing.Quantity += Math.Max(1, entry.Quantity);
                continue;
            }

            var card = new CollectionCard
            {
                Game = game,
                IsFoil = entry.Finish is not null,
                FoilType = entry.Finish,
                Quantity = Math.Max(1, entry.Quantity),
                Condition = condition,
                DateAdded = DateTime.UtcNow,
            };
            ApplyPrinting(card, printing);
            PlaceInTarget(card, target);
            cards[key] = card;
        }

        if (errors.Count > 0)
            return Fail(
                $"Nothing was imported. {Plural(errors.Count, "card")} in \"{deckName}\" couldn't be found — fix {(errors.Count == 1 ? "it" : "them")} on the deck site (or refresh the catalog) and import again.",
                errors);

        return Write(cards.Values.ToList(), target, deckName, null, substitutions);
    }

    private Target? LoadTarget(int locationId)
    {
        using var context = dbFactory.CreateDbContext();
        return context.StorageContainers
            .Where(c => c.Id == locationId)
            .Select(c => new Target(c.Id, c.Name, c.ContainerType, c.Game))
            .FirstOrDefault();
    }

    private ICardGameService? GameService(CardGame game)
    {
        try { return cardService.GetGameService(game); }
        catch (Exception) { return null; }
    }

    private Outcome Write(List<CollectionCard> cards, Target target, string source, string? format,
        List<LocationImportIssueDto> substitutions)
    {
        try
        {
            binderCards.ImportCollectionCards(cards, skipDuplicates: false);
        }
        catch (DeckBoxGameMismatchException ex)
        {
            return Fail($"Nothing was imported. {ex.Message}", []);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Location import into {Location} failed to save", target.Name);
            return Fail("Nothing was imported. The database rejected the import, so no cards were added — try again, and check the server log if it keeps happening.",
                [new LocationImportIssueDto(null, null, ex.GetBaseException().Message)]);
        }

        logger.LogInformation("Imported {Lines} lines ({Copies} copies) from {Source} into location {Location}",
            cards.Count, cards.Sum(c => c.Quantity), source, target.Name);
        return new Outcome(new LocationImportResultDto(source, format, cards.Count, cards.Sum(c => c.Quantity), substitutions), null);
    }

    /// <summary>Confirms a row's catalog id is real and backfills what the file doesn't carry (art,
    /// color, type). False when the id isn't in the catalog.</summary>
    private static bool HydrateFromCatalogId(ICardGameService gs, CollectionCard card)
    {
        object? source;
        try { source = gs.FindCardById(card.GameCardId.Trim()); }
        catch (Exception) { source = null; }
        if (source is null)
            return false;

        card.GameCardId = card.GameCardId.Trim();
        var match = new CardMatch { Source = source };
        card.ImageUri ??= CardImageUriResolver.From(source);
        if (string.IsNullOrEmpty(card.Color)) card.Color = CardAttributeExtractor.ExtractColor(match, card.Game);
        if (string.IsNullOrEmpty(card.CardType)) card.CardType = CardAttributeExtractor.ExtractCardType(match, card.Game);
        return true;
    }

    /// <summary>The exact printing a no-id row describes. TCGplayer rows name the set rather than giving
    /// its code, so match on set name (and number) across the card's printings; otherwise use the
    /// decklist ladder (set + collector, set only, collector only, name only).</summary>
    private static CardMatch? ResolveCsvPrinting(ICardGameService gs, CollectionCard card)
    {
        var name = card.Name.Trim();
        var cn = string.IsNullOrWhiteSpace(card.Number) ? null : card.Number.Trim();
        if (string.IsNullOrWhiteSpace(card.SetCode) && !string.IsNullOrWhiteSpace(card.SetName))
        {
            var setName = card.SetName.Trim();
            return DecklistPrintingResolver.GetPrintingsFuzzy(gs, name).FirstOrDefault(p =>
                string.Equals(p.SetName, setName, StringComparison.OrdinalIgnoreCase)
                && (cn is null || string.Equals(p.CollectorNumber, cn, StringComparison.OrdinalIgnoreCase)));
        }
        return DecklistPrintingResolver.Resolve(gs, new DecklistEntry(card.Quantity, name, card.SetCode, cn));
    }

    private static void ApplyPrinting(CollectionCard card, CardMatch printing)
    {
        card.GameCardId = printing.GameSpecificId;
        card.Name = printing.Name;
        card.SetCode = printing.SetCode;
        card.SetName = printing.SetName;
        card.Number = printing.CollectorNumber;
        card.Rarity = printing.Rarity;
        card.ImageUri = printing.ImageUri;
        card.Color = CardAttributeExtractor.ExtractColor(printing, card.Game);
        card.CardType = CardAttributeExtractor.ExtractCardType(printing, card.Game);
    }

    /// <summary>Every card lands in the target, whatever location the file names. Binder page/slot
    /// positions belong to the source location, so they're dropped (the cards arrive unplaced); a
    /// deck-box section is kept only when importing into a deck box.</summary>
    private static void PlaceInTarget(CollectionCard card, Target target)
    {
        card.ContainerId = target.Id;
        card.Container = null;
        card.Page = null;
        card.Slot = null;
        if (target.Type != ContainerType.DeckBox) card.Section = null;

        // Foil finish default (mirrors CsvExportImportService.ImportCards).
        if (!card.IsFoil) card.FoilType = null;
        else if (string.IsNullOrEmpty(card.FoilType)) card.FoilType = FoilTypes.BasicFoilType(card.Game);
    }

    private static string? NormalizeCondition(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? "NM" : Conditions.GetValueOrDefault(raw.Trim());

    private static string DescribeCsvPrinting(CollectionCard card)
    {
        var set = !string.IsNullOrWhiteSpace(card.SetCode) ? card.SetCode.Trim()
            : !string.IsNullOrWhiteSpace(card.SetName) ? card.SetName.Trim() : null;
        var cn = string.IsNullOrWhiteSpace(card.Number) ? null : card.Number.Trim();
        return (set, cn) switch
        {
            (not null, not null) => $" ({set} #{cn})",
            (not null, null) => $" ({set})",
            (null, not null) => $" (#{cn})",
            _ => "",
        };
    }

    private static string DescribeDeckPrinting(DecklistEntry entry) =>
        (entry.SetCode, entry.CollectorNumber) switch
        {
            ({ Length: > 0 } set, { Length: > 0 } cn) => $" ({set.ToUpperInvariant()} #{cn})",
            ({ Length: > 0 } set, _) => $" ({set.ToUpperInvariant()})",
            _ => "",
        };

    private static string FormatName(CsvFormat format) => format switch
    {
        CsvFormat.AppNative => "OmniCard",
        CsvFormat.TcgPlayer => "TCGplayer",
        CsvFormat.Moxfield => "Moxfield",
        CsvFormat.Manabox => "ManaBox",
        _ => format.ToString(),
    };

    private static string GameName(CardGame game) => game switch
    {
        CardGame.Mtg => "Magic: The Gathering",
        CardGame.OnePiece => "One Piece",
        CardGame.Riftbound => "Riftbound",
        CardGame.Pokemon => "Pokémon",
        CardGame.YuGiOh => "Yu-Gi-Oh!",
        CardGame.FinalFantasy => "Final Fantasy TCG",
        _ => game.ToString(),
    };

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static string LowerFirst(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    private static Outcome Fail(string error, List<LocationImportIssueDto> errors) =>
        new(null, new LocationImportFailureDto(error, errors));
}
