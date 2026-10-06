using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.ImportExport;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Storage;

namespace OmniCard.Collection.ImportExport;

public class CsvExportImportService(
    ICardService? cardService,
    IStorageContainerService? containerService,
    ILogger<CsvExportImportService> logger) : ICsvExportImportService
{
    private static readonly Dictionary<string, string> ConditionToTcgPlayer = new()
    {
        ["NM"] = "Near Mint",
        ["LP"] = "Lightly Played",
        ["MP"] = "Moderately Played",
        ["HP"] = "Heavily Played",
        ["D"] = "Damaged",
        ["DMG"] = "Damaged",
    };

    private static readonly Dictionary<string, string> ConditionToManabox = new()
    {
        ["NM"] = "near_mint",
        ["LP"] = "lightly_played",
        ["MP"] = "moderately_played",
        ["HP"] = "heavily_played",
        ["D"] = "damaged",
        ["DMG"] = "damaged",
    };

    /// <summary>The app's condition codes (the web UI's NM/LP/MP/HP/DMG), accepted by every importer
    /// alongside the format's own spelling.</summary>
    private static readonly Dictionary<string, string> ConditionCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NM"] = "NM", ["LP"] = "LP", ["MP"] = "MP", ["HP"] = "HP", ["DMG"] = "DMG", ["D"] = "DMG",
    };

    /// <summary>TCGplayer / Moxfield spell conditions out ("Near Mint"); Moxfield also has "Mint".</summary>
    private static readonly Dictionary<string, string> TcgPlayerToCondition = new(ConditionCodes, StringComparer.OrdinalIgnoreCase)
    {
        ["Mint"] = "NM",
        ["Near Mint"] = "NM",
        ["Lightly Played"] = "LP",
        ["Moderately Played"] = "MP",
        ["Heavily Played"] = "HP",
        ["Damaged"] = "DMG",
    };

    private static readonly Dictionary<string, string> ManaboxToCondition = new(ConditionCodes, StringComparer.OrdinalIgnoreCase)
    {
        ["mint"] = "NM",
        ["near_mint"] = "NM",
        ["lightly_played"] = "LP",
        ["light_played"] = "LP",
        ["moderately_played"] = "MP",
        ["heavily_played"] = "HP",
        ["damaged"] = "DMG",
    };

    // ── App-Native Export ──

    public void ExportAppNative(string filePath, IEnumerable<CollectionCard> cards)
    {
        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("Game");
        csv.WriteField("GameCardId");
        csv.WriteField("Name");
        csv.WriteField("SetName");
        csv.WriteField("SetCode");
        csv.WriteField("Number");
        csv.WriteField("Rarity");
        csv.WriteField("Condition");
        csv.WriteField("IsFoil");
        csv.WriteField("FoilType");
        csv.WriteField("PurchasePrice");
        csv.WriteField("DateAdded");
        csv.WriteField("ContainerName");
        csv.WriteField("ContainerType");
        csv.WriteField("Page");
        csv.WriteField("Slot");
        csv.WriteField("Section");
        csv.WriteField("Quantity");
        csv.WriteField("Language");
        csv.NextRecord();

        foreach (var card in cards)
        {
            csv.WriteField(card.Game.ToString());
            csv.WriteField(card.GameCardId);
            csv.WriteField(card.Name);
            csv.WriteField(card.SetName);
            csv.WriteField(card.SetCode);
            csv.WriteField(card.Number);
            csv.WriteField(card.Rarity);
            csv.WriteField(card.Condition);
            csv.WriteField(card.IsFoil);
            csv.WriteField(card.FoilType ?? "");
            csv.WriteField(card.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? "");
            csv.WriteField(card.DateAdded.ToString("o"));
            csv.WriteField(card.Container?.Name ?? "");
            csv.WriteField(card.Container?.ContainerType.ToString() ?? "");
            csv.WriteField(card.Page?.ToString() ?? "");
            csv.WriteField(card.Slot?.ToString() ?? "");
            csv.WriteField(card.Section ?? "");
            csv.WriteField(card.Quantity);
            csv.WriteField(card.Language);
            csv.NextRecord();
        }

        logger.LogInformation("Exported {Count} cards in app-native format to {Path}", cards.Count(), filePath);
    }

    // ── Card Price Ticker Export (Premiere Pro plug-in: Card Name + Market Price only) ──

    public void ExportPriceTicker(string filePath, IEnumerable<CollectionCard> cards)
    {
        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("Card Name");
        csv.WriteField("Market Price");
        csv.NextRecord();

        foreach (var card in cards)
        {
            csv.WriteField(card.Name);
            csv.WriteField(card.MarketPrice.ToString(CultureInfo.InvariantCulture));
            csv.NextRecord();
        }

        logger.LogInformation("Exported {Count} cards in Card Price Ticker format to {Path}", cards.Count(), filePath);
    }

    // ── TCGPlayer Export ──

    public void ExportTcgPlayer(string filePath, IEnumerable<CollectionCard> cards)
    {
        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("Quantity");
        csv.WriteField("Name");
        csv.WriteField("Set Name");
        csv.WriteField("Number");
        csv.WriteField("Condition");
        csv.WriteField("Printing");
        csv.WriteField("Price");
        csv.NextRecord();

        foreach (var card in cards)
        {
            csv.WriteField(1);
            csv.WriteField(card.Name);
            csv.WriteField(card.SetName);
            csv.WriteField(card.Number);
            csv.WriteField(ConditionToTcgPlayer.GetValueOrDefault(card.Condition, card.Condition));
            csv.WriteField(card.IsFoil ? "Foil" : "Normal");
            csv.WriteField(card.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? "");
            csv.NextRecord();
        }

        logger.LogInformation("Exported {Count} cards in TCGPlayer format to {Path}", cards.Count(), filePath);
    }

    // ── Moxfield Export ──

    public void ExportMoxfield(string filePath, IEnumerable<CollectionCard> cards)
    {
        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("Count");
        csv.WriteField("Name");
        csv.WriteField("Edition");
        csv.WriteField("Collector Number");
        csv.WriteField("Condition");
        csv.WriteField("Foil");
        csv.WriteField("Purchase Price");
        csv.NextRecord();

        foreach (var card in cards)
        {
            csv.WriteField(1);
            csv.WriteField(card.Name);
            csv.WriteField(card.SetCode.ToUpperInvariant());
            csv.WriteField(card.Number);
            csv.WriteField(card.Condition);
            csv.WriteField(card.IsFoil ? "foil" : "");
            csv.WriteField(card.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? "");
            csv.NextRecord();
        }

        logger.LogInformation("Exported {Count} cards in Moxfield format to {Path}", cards.Count(), filePath);
    }

    // ── Manabox / Mythic Tools Export ──

    public void ExportManabox(string filePath, IEnumerable<CollectionCard> cards)
    {
        logger.LogInformation("Exporting collection to ManaBox CSV: {FilePath}", filePath);
        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

        // Header
        csv.WriteField("Name");
        csv.WriteField("Set code");
        csv.WriteField("Set name");
        csv.WriteField("Collector number");
        csv.WriteField("Foil");
        csv.WriteField("Rarity");
        csv.WriteField("Quantity");
        csv.WriteField("Scryfall ID");
        csv.WriteField("Purchase price");
        csv.WriteField("Misprint");
        csv.WriteField("Altered");
        csv.WriteField("Condition");
        csv.WriteField("Language");
        csv.WriteField("Purchase price currency");
        csv.WriteField("Added");
        csv.NextRecord();

        foreach (var card in cards)
        {
            csv.WriteField(card.Name);
            csv.WriteField(card.SetCode);
            csv.WriteField(card.SetName);
            csv.WriteField(card.Number);
            csv.WriteField(card.IsFoil ? "foil" : "normal");
            csv.WriteField(card.Rarity);
            csv.WriteField(1);
            csv.WriteField(card.GameCardId);
            csv.WriteField(card.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? "");
            csv.WriteField(false);
            csv.WriteField(false);
            csv.WriteField(ConditionToManabox.GetValueOrDefault(card.Condition, "near_mint"));
            csv.WriteField(card.Language);
            csv.WriteField("USD");
            csv.WriteField(card.DateAdded.ToString("o"));
            csv.NextRecord();
        }

        logger.LogInformation("ManaBox CSV export complete");
    }

    // ── Archidekt Export (collection CSV; Archidekt's importer maps these columns by header) ──

    public void ExportArchidekt(string filePath, IEnumerable<CollectionCard> cards)
    {
        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("Quantity");
        csv.WriteField("Name");
        csv.WriteField("Finish");
        csv.WriteField("Condition");
        csv.WriteField("Date Added");
        csv.WriteField("Language");
        csv.WriteField("Purchase Price");
        csv.WriteField("Tags");
        csv.WriteField("Edition Name");
        csv.WriteField("Edition Code");
        csv.WriteField("Scryfall ID");
        csv.WriteField("Collector Number");
        csv.NextRecord();

        var count = 0;
        foreach (var card in cards)
        {
            csv.WriteField(Math.Max(1, card.Quantity));
            csv.WriteField(card.Name);
            csv.WriteField(!card.IsFoil ? "Normal" : IsEtched(card) ? "Etched" : "Foil");
            csv.WriteField(ConditionCodes.TryGetValue(card.Condition ?? "", out var cond) ? (cond == "DMG" ? "D" : cond) : "NM");
            csv.WriteField(card.DateAdded.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            csv.WriteField((CardLanguages.Normalize(card.Language) ?? CardLanguages.English).ToUpperInvariant());
            csv.WriteField(card.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? "");
            csv.WriteField(string.Join(",", card.Tags));
            csv.WriteField(card.SetName);
            csv.WriteField(card.SetCode.ToLowerInvariant());
            // Archidekt is MTG-only; only an MTG card's id is a Scryfall id.
            csv.WriteField(card.Game == CardGame.Mtg ? card.GameCardId : "");
            csv.WriteField(card.Number);
            csv.NextRecord();
            count++;
        }

        logger.LogInformation("Exported {Count} cards in Archidekt format to {Path}", count, filePath);
    }

    // ── Deckbox.org Export ──

    private static readonly Dictionary<string, string> ConditionToDeckbox = new()
    {
        ["NM"] = "Near Mint",
        ["LP"] = "Good (Lightly Played)",
        ["MP"] = "Played",
        ["HP"] = "Heavily Played",
        ["D"] = "Poor",
        ["DMG"] = "Poor",
    };

    public void ExportDeckbox(string filePath, IEnumerable<CollectionCard> cards)
    {
        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("Count");
        csv.WriteField("Tradelist Count");
        csv.WriteField("Name");
        csv.WriteField("Edition");
        csv.WriteField("Card Number");
        csv.WriteField("Condition");
        csv.WriteField("Language");
        csv.WriteField("Foil");
        csv.WriteField("Signed");
        csv.WriteField("Artist Proof");
        csv.WriteField("Altered Art");
        csv.WriteField("Misprint");
        csv.WriteField("Promo");
        csv.WriteField("Textless");
        csv.WriteField("My Price");
        csv.NextRecord();

        var count = 0;
        foreach (var card in cards)
        {
            csv.WriteField(Math.Max(1, card.Quantity));
            csv.WriteField(0);
            csv.WriteField(card.Name);
            csv.WriteField(card.SetName);
            csv.WriteField(card.Number);
            csv.WriteField(ConditionToDeckbox.GetValueOrDefault(card.Condition ?? "", "Near Mint"));
            csv.WriteField(LanguageName(card.Language));
            csv.WriteField(card.IsFoil ? "foil" : "");
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField("");
            csv.WriteField(card.PurchasePrice?.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
            csv.NextRecord();
            count++;
        }

        logger.LogInformation("Exported {Count} cards in Deckbox format to {Path}", count, filePath);
    }

    // ── Dragon Shield Card Manager Export ──
    // Dragon Shield grades on the Cardmarket scale; the app's TCGplayer-style codes map onto it.

    private static readonly Dictionary<string, string> ConditionToDragonShield = new()
    {
        ["NM"] = "NearMint",
        ["LP"] = "Excellent",
        ["MP"] = "Good",
        ["HP"] = "Played",
        ["D"] = "Poor",
        ["DMG"] = "Poor",
    };

    public void ExportDragonShield(string filePath, IEnumerable<CollectionCard> cards)
    {
        using var writer = new StreamWriter(filePath);
        // Dragon Shield's own exports open with an Excel separator hint; its importer expects it.
        writer.WriteLine("\"sep=,\"");
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("Folder Name");
        csv.WriteField("Quantity");
        csv.WriteField("Trade Quantity");
        csv.WriteField("Card Name");
        csv.WriteField("Set Code");
        csv.WriteField("Set Name");
        csv.WriteField("Card Number");
        csv.WriteField("Condition");
        csv.WriteField("Printing");
        csv.WriteField("Language");
        csv.WriteField("Price Bought");
        csv.WriteField("Date Bought");
        csv.NextRecord();

        var count = 0;
        foreach (var card in cards)
        {
            csv.WriteField(card.Container?.Name ?? "OmniCard");
            csv.WriteField(Math.Max(1, card.Quantity));
            csv.WriteField(0);
            csv.WriteField(card.Name);
            csv.WriteField(card.SetCode.ToUpperInvariant());
            csv.WriteField(card.SetName);
            csv.WriteField(card.Number);
            csv.WriteField(ConditionToDragonShield.GetValueOrDefault(card.Condition ?? "", "NearMint"));
            csv.WriteField(!card.IsFoil ? "Normal" : IsEtched(card) ? "Etched" : "Foil");
            csv.WriteField(LanguageName(card.Language));
            csv.WriteField(card.PurchasePrice?.ToString("0.00", CultureInfo.InvariantCulture) ?? "");
            csv.WriteField(card.DateAdded.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            csv.NextRecord();
            count++;
        }

        logger.LogInformation("Exported {Count} cards in Dragon Shield format to {Path}", count, filePath);
    }

    // ── Plain-text card list ("4 Lightning Bolt (2X2) 117 *F*") — pastes into Moxfield, Archidekt,
    // ManaBox and most deck builders ──

    public void ExportTextList(string filePath, IEnumerable<CollectionCard> cards)
    {
        using var writer = new StreamWriter(filePath);
        var count = 0;
        foreach (var card in cards)
        {
            writer.WriteLine(TextListLine(card));
            count++;
        }

        logger.LogInformation("Exported {Count} cards as a text list to {Path}", count, filePath);
    }

    /// <summary>One text-list line: quantity, name, (SET) and collector number when known, then
    /// <c>*F*</c> (foil) or <c>*E*</c> (etched).</summary>
    public static string TextListLine(CollectionCard card)
    {
        var line = $"{Math.Max(1, card.Quantity)} {card.Name}";
        if (!string.IsNullOrWhiteSpace(card.SetCode))
        {
            line += $" ({card.SetCode.ToUpperInvariant()})";
            if (!string.IsNullOrWhiteSpace(card.Number))
                line += $" {card.Number}";
        }
        if (card.IsFoil)
            line += IsEtched(card) ? " *E*" : " *F*";
        return line;
    }

    private static bool IsEtched(CollectionCard card) =>
        card.FoilType?.Contains("etched", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>English name of a card language code, as Deckbox / Dragon Shield spell it.</summary>
    private static string LanguageName(string? language) => (CardLanguages.Normalize(language) ?? CardLanguages.English) switch
    {
        "ja" => "Japanese",
        "de" => "German",
        "fr" => "French",
        "it" => "Italian",
        "es" => "Spanish",
        "pt" => "Portuguese",
        "ko" => "Korean",
        "ru" => "Russian",
        "zhs" => "Chinese Simplified",
        "zht" => "Chinese Traditional",
        "th" => "Thai",
        "id" => "Indonesian",
        "ph" => "Phyrexian",
        _ => "English",
    };

    public void ExportManaboxScans(string filePath, IEnumerable<ScannedCard> scans)
    {
        logger.LogInformation("Exporting scan queue to ManaBox CSV: {FilePath}", filePath);
        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

        csv.WriteField("Name");
        csv.WriteField("Set code");
        csv.WriteField("Set name");
        csv.WriteField("Collector number");
        csv.WriteField("Foil");
        csv.WriteField("Rarity");
        csv.WriteField("Quantity");
        csv.WriteField("Scryfall ID");
        csv.WriteField("Purchase price");
        csv.WriteField("Misprint");
        csv.WriteField("Altered");
        csv.WriteField("Condition");
        csv.WriteField("Language");
        csv.WriteField("Purchase price currency");
        csv.WriteField("Added");
        csv.NextRecord();

        var now = DateTime.UtcNow.ToString("o");
        foreach (var scan in scans)
        {
            if (scan.Match is null) continue;

            csv.WriteField(scan.Match.Name);
            csv.WriteField(scan.Match.SetCode);
            csv.WriteField(scan.Match.SetName);
            csv.WriteField(scan.Match.CollectorNumber);
            csv.WriteField(scan.IsFoil ? "foil" : "normal");
            csv.WriteField(scan.Match.Rarity);
            csv.WriteField(1);
            csv.WriteField(scan.Match.GameSpecificId);
            csv.WriteField(scan.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? "");
            csv.WriteField(false);
            csv.WriteField(false);
            csv.WriteField(ConditionToManabox.GetValueOrDefault(scan.Condition, "near_mint"));
            csv.WriteField("en");
            csv.WriteField("USD");
            csv.WriteField(now);
            csv.NextRecord();
        }

        logger.LogInformation("ManaBox scan CSV export complete");
    }

    public void ExportManaboxScansCollection(string filePath, IEnumerable<ScannedCard> scans)
    {
        logger.LogInformation("Exporting scan queue to ManaBox collection CSV: {FilePath}", filePath);
        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

        csv.WriteField("Binder Name");
        csv.WriteField("Binder Type");
        csv.WriteField("Name");
        csv.WriteField("Set code");
        csv.WriteField("Set name");
        csv.WriteField("Collector number");
        csv.WriteField("Foil");
        csv.WriteField("Rarity");
        csv.WriteField("Quantity");
        csv.WriteField("Scryfall ID");
        csv.WriteField("Purchase price");
        csv.WriteField("Misprint");
        csv.WriteField("Altered");
        csv.WriteField("Condition");
        csv.WriteField("Language");
        csv.WriteField("Purchase price currency");
        csv.WriteField("Added");
        csv.NextRecord();

        var now = DateTime.UtcNow.ToString("o");
        foreach (var scan in scans)
        {
            if (scan.Match is null) continue;

            csv.WriteField(scan.OverrideContainer?.Name ?? "Scans");
            csv.WriteField(scan.OverrideContainer?.ContainerType.ToString().ToLowerInvariant() ?? "list");
            csv.WriteField(scan.Match.Name);
            csv.WriteField(scan.Match.SetCode);
            csv.WriteField(scan.Match.SetName);
            csv.WriteField(scan.Match.CollectorNumber);
            csv.WriteField(scan.IsFoil ? "foil" : "normal");
            csv.WriteField(scan.Match.Rarity);
            csv.WriteField(1);
            csv.WriteField(scan.Match.GameSpecificId);
            csv.WriteField(scan.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? "");
            csv.WriteField(false);
            csv.WriteField(false);
            csv.WriteField(ConditionToManabox.GetValueOrDefault(scan.Condition, "near_mint"));
            csv.WriteField("en");
            csv.WriteField("USD");
            csv.WriteField(now);
            csv.NextRecord();
        }

        logger.LogInformation("ManaBox scan collection CSV export complete");
    }

    public void ExportManaboxScansText(string filePath, IEnumerable<ScannedCard> scans)
    {
        logger.LogInformation("Exporting scan queue to ManaBox text: {FilePath}", filePath);
        using var writer = new StreamWriter(filePath);

        foreach (var scan in scans)
        {
            if (scan.Match is null) continue;

            var line = $"1 {scan.Match.Name} ({scan.Match.SetCode}) {scan.Match.CollectorNumber}";
            if (scan.IsFoil)
                line += " *F*";
            writer.WriteLine(line);
        }

        logger.LogInformation("ManaBox scan text export complete");
    }

    // ── Import ──

    public CsvImportPreview PreviewImport(string filePath)
    {
        logger.LogInformation("Previewing import from {Path}", filePath);
        using var reader = new StreamReader(filePath);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null, // silently ignore missing fields
        };
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];
        var headerSet = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);

        var format = DetectFormat(headerSet);
        if (format is null)
        {
            return new CsvImportPreview
            {
                DetectedFormat = CsvFormat.AppNative,
                FormatRecognized = false,
                Headers = [.. headers],
                Warnings = ["Unrecognized CSV format"],
                TotalRows = 0,
            };
        }

        var cards = new List<CollectionCard>();
        var cardRows = new List<int>();
        var warnings = new List<string>();
        var issues = new List<CsvRowIssue>();
        var totalRows = 0;

        while (csv.Read())
        {
            totalRows++;
            var row = csv.Parser.Row;
            var rowIssues = new List<string>();
            try
            {
                var card = format.Value switch
                {
                    CsvFormat.AppNative => ParseAppNativeRow(csv, rowIssues),
                    CsvFormat.TcgPlayer => ParseTcgPlayerRow(csv, rowIssues),
                    CsvFormat.Moxfield => ParseMoxfieldRow(csv, rowIssues),
                    CsvFormat.Manabox => ParseManaboxRow(csv, rowIssues),
                    _ => null,
                };

                if (card is not null)
                {
                    cards.Add(card);
                    cardRows.Add(row);
                    issues.AddRange(rowIssues.Select(m => new CsvRowIssue(row, NullIfBlank(card.Name), m)));
                }
                else
                {
                    warnings.Add($"Row {totalRows}: could not parse card");
                    issues.Add(new CsvRowIssue(row, null, "This row couldn't be read as a card."));
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Row {totalRows}: {ex.Message}");
                issues.Add(new CsvRowIssue(row, NullIfBlank(TryGetField(csv, "Name")), ex.Message));
            }
        }

        logger.LogInformation("Preview complete: {Format} format, {Count} cards, {Warnings} warnings", format, cards.Count, warnings.Count);

        return new CsvImportPreview
        {
            DetectedFormat = format.Value,
            Headers = [.. headers],
            Cards = cards,
            CardRows = cardRows,
            Warnings = warnings,
            Issues = issues,
            TotalRows = totalRows,
        };
    }

    public int ImportCards(CsvImportPreview preview, bool skipDuplicates, int? targetContainerId = null, string? defaultFoilType = null)
    {
        logger.LogInformation("Importing {Count} cards (skipDuplicates={Skip}, container={Container}, defaultFoilType={FoilType})",
            preview.Cards.Count, skipDuplicates, targetContainerId, defaultFoilType);

        foreach (var card in preview.Cards)
        {
            // Foil finish: a value parsed from the file wins; otherwise a foil card without a finish
            // gets the dialog's default treatment (or the game's basic finish). Non-foil stays null.
            if (!card.IsFoil)
                card.FoilType = null;
            else if (string.IsNullOrEmpty(card.FoilType))
                card.FoilType = defaultFoilType ?? FoilTypes.BasicFoilType(card.Game);

            // Resolve container: use target if specified, otherwise resolve from app-native data
            if (targetContainerId is not null && card.ContainerId is null)
            {
                card.ContainerId = targetContainerId.Value;
                card.Container = null;
            }
            else if (card.Container is not null && card.ContainerId is null)
            {
                var existing = containerService!.GetAll().FirstOrDefault(c => c.Name == card.Container.Name);
                if (existing is null)
                {
                    existing = containerService.Create(card.Container.Name, card.Container.ContainerType);
                }
                card.ContainerId = existing.Id;
                card.Container = null; // Clear nav property for EF insert
            }
        }

        var imported = cardService!.ImportCollectionCards(preview.Cards, skipDuplicates);
        logger.LogInformation("Imported {Count} cards", imported);
        return imported;
    }

    // ── Format Detection ──

    internal static CsvFormat? DetectFormat(HashSet<string> headers)
    {
        if (headers.Contains("GameCardId"))
            return CsvFormat.AppNative;
        if (headers.Contains("Printing"))
            return CsvFormat.TcgPlayer;
        if (headers.Contains("Edition"))
            return CsvFormat.Moxfield;
        if (headers.Contains("Foil") && headers.Contains("Scryfall ID") && headers.Contains("Purchase price currency"))
            return CsvFormat.Manabox;
        return null;
    }

    // ── Row Parsers ──
    // Each parser appends to `issues` any value it had to default (the lenient import ignores these;
    // the all-or-nothing location import rejects the file over them).

    private static CollectionCard ParseAppNativeRow(CsvReader csv, List<string> issues)
    {
        var gameRaw = csv.GetField("Game");
        if (!Enum.TryParse<CardGame>(gameRaw, ignoreCase: true, out var game) || !Enum.IsDefined(game))
            throw new FormatException(
                $"Unknown game '{gameRaw}'. Expected one of: {string.Join(", ", Enum.GetNames<CardGame>())}.");

        var card = new CollectionCard
        {
            Game = game,
            GameCardId = csv.GetField("GameCardId") ?? "",
            Name = csv.GetField("Name") ?? "",
            SetName = csv.GetField("SetName") ?? "",
            SetCode = csv.GetField("SetCode") ?? "",
            Number = csv.GetField("Number") ?? "",
            Rarity = csv.GetField("Rarity") ?? "",
            Condition = csv.GetField("Condition") is { Length: > 0 } cond ? cond : "NM",
            IsFoil = bool.TryParse(csv.GetField("IsFoil"), out var foil) && foil,
            FoilType = csv.GetField("FoilType") is { Length: > 0 } ft ? ft : null,
            PurchasePrice = decimal.TryParse(csv.GetField("PurchasePrice"), CultureInfo.InvariantCulture, out var price) ? price : null,
            DateAdded = DateTime.TryParse(csv.GetField("DateAdded"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : DateTime.UtcNow,
            Page = int.TryParse(csv.GetField("Page"), out var page) ? page : null,
            Slot = int.TryParse(csv.GetField("Slot"), out var slot) ? slot : null,
            Section = csv.GetField("Section") is { Length: > 0 } sec ? sec : null,
            Quantity = ParseQuantity(csv, "Quantity", issues),
            Language = ParseLanguage(csv, issues),
        };

        var containerName = csv.GetField("ContainerName");
        var containerTypeStr = csv.GetField("ContainerType");
        if (!string.IsNullOrEmpty(containerName))
        {
            var containerType = Enum.TryParse<ContainerType>(containerTypeStr, ignoreCase: true, out var ct) ? ct : ContainerType.Box;
            card.Container = new StorageContainer { Name = containerName, ContainerType = containerType };
        }

        return card;
    }

    private static CollectionCard ParseTcgPlayerRow(CsvReader csv, List<string> issues)
    {
        return new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = "", // Needs resolution — handled externally or left empty
            Name = csv.GetField("Name") ?? "",
            SetName = csv.GetField("Set Name") ?? "",
            SetCode = "",
            Number = csv.GetField("Number") ?? "",
            Rarity = "",
            Condition = MapCondition(csv.GetField("Condition"), TcgPlayerToCondition, issues),
            IsFoil = csv.GetField("Printing") == "Foil",
            PurchasePrice = decimal.TryParse(csv.GetField("Price"), CultureInfo.InvariantCulture, out var price) ? price : null,
            DateAdded = DateTime.UtcNow,
            Quantity = ParseQuantity(csv, "Quantity", issues),
            Language = ParseLanguage(csv, issues),
        };
    }

    private static CollectionCard ParseMoxfieldRow(CsvReader csv, List<string> issues)
    {
        return new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = "",
            Name = csv.GetField("Name") ?? "",
            SetName = "",
            SetCode = csv.GetField("Edition") ?? "",
            Number = csv.GetField("Collector Number") ?? "",
            Rarity = "",
            Condition = MapCondition(csv.GetField("Condition"), TcgPlayerToCondition, issues),
            IsFoil = csv.GetField("Foil") == "foil",
            PurchasePrice = decimal.TryParse(csv.GetField("Purchase Price"), CultureInfo.InvariantCulture, out var price) ? price : null,
            DateAdded = DateTime.UtcNow,
            Quantity = ParseQuantity(csv, "Count", issues),
            Language = ParseLanguage(csv, issues),
        };
    }

    private static CollectionCard ParseManaboxRow(CsvReader csv, List<string> issues)
    {
        var scryfallId = csv.GetField("Scryfall ID");

        return new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = !string.IsNullOrEmpty(scryfallId) ? scryfallId : "",
            Name = csv.GetField("Name") ?? "",
            SetName = csv.GetField("Set name") ?? "",
            SetCode = csv.GetField("Set code") ?? "",
            Number = csv.GetField("Collector number") ?? "",
            Rarity = csv.GetField("Rarity") ?? "",
            Condition = MapCondition(csv.GetField("Condition"), ManaboxToCondition, issues),
            IsFoil = csv.GetField("Foil") == "foil",
            PurchasePrice = decimal.TryParse(csv.GetField("Purchase price"), CultureInfo.InvariantCulture, out var price) ? price : null,
            DateAdded = DateTime.UtcNow,
            Quantity = ParseQuantity(csv, "Quantity", issues),
            Language = ParseLanguage(csv, issues),
        };
    }

    /// <summary>The row's "Language" column (codes like "ja" or names like "Japanese"). Missing/blank
    /// means English; an unrecognized value is read as English and reported.</summary>
    private static string ParseLanguage(CsvReader csv, List<string> issues)
    {
        var raw = csv.GetField("Language");
        if (string.IsNullOrWhiteSpace(raw))
            return CardLanguages.English;
        if (CardLanguages.Normalize(raw) is { } code)
            return code;
        issues.Add($"Unrecognized language '{raw}'. Use a code like en, ja, de, fr, it, es, pt, ko, zhs.");
        return CardLanguages.English;
    }

    /// <summary>A blank condition means NM; an unrecognized one is read as NM and reported.</summary>
    private static string MapCondition(string? raw, Dictionary<string, string> map, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "NM";
        if (map.TryGetValue(raw.Trim(), out var condition))
            return condition;
        issues.Add($"Unrecognized condition '{raw}'. Use one of: {string.Join(", ", map.Keys)}.");
        return "NM";
    }

    /// <summary>A missing/blank quantity means 1; anything but a whole number of 1+ is read as 1 and reported.</summary>
    private static int ParseQuantity(CsvReader csv, string column, List<string> issues)
    {
        var raw = csv.GetField(column);
        if (string.IsNullOrWhiteSpace(raw))
            return 1;
        if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) && qty > 0)
            return qty;
        issues.Add($"{column} '{raw}' must be a whole number of 1 or more.");
        return 1;
    }

    private static string? TryGetField(CsvReader csv, string name)
    {
        try { return csv.GetField(name); }
        catch { return null; }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
