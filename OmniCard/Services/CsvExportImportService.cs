using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Services;

public interface ICsvExportImportService
{
    void ExportAppNative(string filePath, IEnumerable<CollectionCard> cards);
    void ExportTcgPlayer(string filePath, IEnumerable<CollectionCard> cards);
    void ExportMoxfield(string filePath, IEnumerable<CollectionCard> cards);
    void ExportManabox(string filePath, IEnumerable<CollectionCard> cards);
    CsvImportPreview PreviewImport(string filePath);
    int ImportCards(CsvImportPreview preview, bool skipDuplicates, int? targetContainerId = null);
}

public class CsvExportImportService(
    IDbContextFactory<CollectionDbContext>? dbContextFactory,
    IScryfallService? scryfallService,
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
    };

    private static readonly Dictionary<string, string> TcgPlayerToCondition =
        ConditionToTcgPlayer.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);

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
        csv.WriteField("PurchasePrice");
        csv.WriteField("DateAdded");
        csv.WriteField("ContainerName");
        csv.WriteField("ContainerType");
        csv.WriteField("Page");
        csv.WriteField("Slot");
        csv.WriteField("Section");
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
            csv.WriteField(card.PurchasePrice?.ToString(CultureInfo.InvariantCulture) ?? "");
            csv.WriteField(card.DateAdded.ToString("o"));
            csv.WriteField(card.Container?.Name ?? "");
            csv.WriteField(card.Container?.ContainerType.ToString() ?? "");
            csv.WriteField(card.Page?.ToString() ?? "");
            csv.WriteField(card.Slot?.ToString() ?? "");
            csv.WriteField(card.Section ?? "");
            csv.NextRecord();
        }

        logger.LogInformation("Exported {Count} cards in app-native format to {Path}", cards.Count(), filePath);
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
        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        csv.WriteField("Card Name");
        csv.WriteField("Set Code");
        csv.WriteField("Set Name");
        csv.WriteField("Collector Number");
        csv.WriteField("Rarity");
        csv.WriteField("Language");
        csv.WriteField("Quantity");
        csv.WriteField("Condition");
        csv.WriteField("Finish");
        csv.WriteField("Altered");
        csv.WriteField("Signed");
        csv.WriteField("Misprint");
        csv.WriteField("Price (USD)");
        csv.WriteField("Price (EUR)");
        csv.WriteField("Price (USD Foil)");
        csv.WriteField("Price (EUR Foil)");
        csv.WriteField("Price (USD Etched)");
        csv.WriteField("Price (EUR Etched)");
        csv.WriteField("Scryfall ID");
        csv.WriteField("Container Type");
        csv.WriteField("Container Name");
        csv.NextRecord();

        foreach (var card in cards)
        {
            csv.WriteField(card.Name);
            csv.WriteField(card.SetCode);
            csv.WriteField(card.SetName);
            csv.WriteField(card.Number);
            csv.WriteField(card.Rarity);
            csv.WriteField("en");
            csv.WriteField(1);
            csv.WriteField(card.Condition);
            csv.WriteField(card.IsFoil ? "foil" : "nonfoil");
            csv.WriteField(false);
            csv.WriteField(false);
            csv.WriteField(false);
            csv.WriteField(""); // Price (USD)
            csv.WriteField(""); // Price (EUR)
            csv.WriteField(""); // Price (USD Foil)
            csv.WriteField(""); // Price (EUR Foil)
            csv.WriteField(""); // Price (USD Etched)
            csv.WriteField(""); // Price (EUR Etched)
            csv.WriteField(card.GameCardId);
            csv.WriteField("list");
            csv.WriteField(card.Container?.Name ?? "recent");
            csv.NextRecord();
        }

        logger.LogInformation("Exported {Count} cards in Manabox format to {Path}", cards.Count(), filePath);
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
                Warnings = ["Unrecognized CSV format"],
                TotalRows = 0,
            };
        }

        var cards = new List<CollectionCard>();
        var warnings = new List<string>();
        var totalRows = 0;

        while (csv.Read())
        {
            totalRows++;
            try
            {
                var card = format.Value switch
                {
                    CsvFormat.AppNative => ParseAppNativeRow(csv),
                    CsvFormat.TcgPlayer => ParseTcgPlayerRow(csv),
                    CsvFormat.Moxfield => ParseMoxfieldRow(csv),
                    CsvFormat.Manabox => ParseManaboxRow(csv),
                    _ => null,
                };

                if (card is not null)
                    cards.Add(card);
                else
                    warnings.Add($"Row {totalRows}: could not parse card");
            }
            catch (Exception ex)
            {
                warnings.Add($"Row {totalRows}: {ex.Message}");
            }
        }

        logger.LogInformation("Preview complete: {Format} format, {Count} cards, {Warnings} warnings", format, cards.Count, warnings.Count);

        return new CsvImportPreview
        {
            DetectedFormat = format.Value,
            Cards = cards,
            Warnings = warnings,
            TotalRows = totalRows,
        };
    }

    public int ImportCards(CsvImportPreview preview, bool skipDuplicates, int? targetContainerId = null)
    {
        logger.LogInformation("Importing {Count} cards (skipDuplicates={Skip}, container={Container})",
            preview.Cards.Count, skipDuplicates, targetContainerId);
        using var context = dbContextFactory!.CreateDbContext();

        var imported = 0;
        foreach (var card in preview.Cards)
        {
            if (skipDuplicates)
            {
                var exists = context.Cards.Any(c =>
                    c.Game == card.Game &&
                    c.GameCardId == card.GameCardId &&
                    c.Condition == card.Condition &&
                    c.IsFoil == card.IsFoil);
                if (exists)
                    continue;
            }

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

            context.Cards.Add(card);
            imported++;
        }

        context.SaveChanges();
        logger.LogInformation("Imported {Count} cards", imported);
        return imported;
    }

    // ── Format Detection ──

    private static CsvFormat? DetectFormat(HashSet<string> headers)
    {
        if (headers.Contains("GameCardId"))
            return CsvFormat.AppNative;
        if (headers.Contains("Printing"))
            return CsvFormat.TcgPlayer;
        if (headers.Contains("Edition"))
            return CsvFormat.Moxfield;
        if (headers.Contains("Finish") && headers.Contains("Card Name"))
            return CsvFormat.Manabox;
        return null;
    }

    // ── Row Parsers ──

    private static CollectionCard ParseAppNativeRow(CsvReader csv)
    {
        var card = new CollectionCard
        {
            Game = Enum.Parse<CardGame>(csv.GetField("Game")!, ignoreCase: true),
            GameCardId = csv.GetField("GameCardId") ?? "",
            Name = csv.GetField("Name") ?? "",
            SetName = csv.GetField("SetName") ?? "",
            SetCode = csv.GetField("SetCode") ?? "",
            Number = csv.GetField("Number") ?? "",
            Rarity = csv.GetField("Rarity") ?? "",
            Condition = csv.GetField("Condition") ?? "NM",
            IsFoil = bool.TryParse(csv.GetField("IsFoil"), out var foil) && foil,
            PurchasePrice = decimal.TryParse(csv.GetField("PurchasePrice"), CultureInfo.InvariantCulture, out var price) ? price : null,
            DateAdded = DateTime.TryParse(csv.GetField("DateAdded"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : DateTime.UtcNow,
            Page = int.TryParse(csv.GetField("Page"), out var page) ? page : null,
            Slot = int.TryParse(csv.GetField("Slot"), out var slot) ? slot : null,
            Section = csv.GetField("Section") is { Length: > 0 } sec ? sec : null,
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

    private static CollectionCard ParseTcgPlayerRow(CsvReader csv)
    {
        var conditionRaw = csv.GetField("Condition") ?? "Near Mint";
        var condition = TcgPlayerToCondition.GetValueOrDefault(conditionRaw, "NM");

        return new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = "", // Needs resolution — handled externally or left empty
            Name = csv.GetField("Name") ?? "",
            SetName = csv.GetField("Set Name") ?? "",
            SetCode = "",
            Number = csv.GetField("Number") ?? "",
            Rarity = "",
            Condition = condition,
            IsFoil = csv.GetField("Printing") == "Foil",
            PurchasePrice = decimal.TryParse(csv.GetField("Price"), CultureInfo.InvariantCulture, out var price) ? price : null,
            DateAdded = DateTime.UtcNow,
        };
    }

    private static CollectionCard ParseMoxfieldRow(CsvReader csv)
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
            Condition = csv.GetField("Condition") ?? "NM",
            IsFoil = csv.GetField("Foil") == "foil",
            PurchasePrice = decimal.TryParse(csv.GetField("Purchase Price"), CultureInfo.InvariantCulture, out var price) ? price : null,
            DateAdded = DateTime.UtcNow,
        };
    }

    private static CollectionCard ParseManaboxRow(CsvReader csv)
    {
        var scryfallId = csv.GetField("Scryfall ID");

        return new CollectionCard
        {
            Game = CardGame.Mtg,
            GameCardId = !string.IsNullOrEmpty(scryfallId) ? scryfallId : "",
            Name = csv.GetField("Card Name") ?? "",
            SetName = csv.GetField("Set Name") ?? "",
            SetCode = csv.GetField("Set Code") ?? "",
            Number = csv.GetField("Collector Number") ?? "",
            Rarity = csv.GetField("Rarity") ?? "",
            Condition = csv.GetField("Condition") ?? "NM",
            IsFoil = csv.GetField("Finish") == "foil",
            PurchasePrice = decimal.TryParse(csv.GetField("Price (USD)"), CultureInfo.InvariantCulture, out var price) ? price : null,
            DateAdded = DateTime.UtcNow,
        };
    }
}
