using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

/// <summary>
/// Reads and writes <c>.ocss</c> scan-session files: a zip of the scan images plus a JSON manifest of
/// each pending card's match + user edits. On open, each card's matched catalog object and per-card
/// override container are re-resolved from the live data so a reopened card commits identically to one
/// scanned fresh. Also owns the single crash-recovery autosave.
/// </summary>
public sealed class ScanSessionService : IScanSessionService
{
    private const string ManifestEntry = "session.json";
    private const string ImagesDir = "images";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IDataPathService _dataPaths;
    private readonly IStorageContainerService _storage;
    private readonly Dictionary<CardGame, ICardGameService> _gameServices;
    private readonly ILogger<ScanSessionService> _logger;

    // Serialize writes to the single recovery file so overlapping autosaves can't corrupt it.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ScanSessionService(
        IDataPathService dataPaths,
        IStorageContainerService storage,
        IEnumerable<ICardGameService> gameServices,
        ILogger<ScanSessionService> logger)
    {
        _dataPaths = dataPaths;
        _storage = storage;
        _gameServices = gameServices.ToDictionary(s => s.Game);
        _logger = logger;
    }

    public string FileExtension => ".ocss";
    public string FileDialogFilter => "OmniCard Scan Session (*.ocss)|*.ocss";

    public Task SaveAsync(ScanSession session, IReadOnlyList<ScannedCard> cards, string filePath, CancellationToken ct = default)
        => WriteAsync(session, cards, filePath, ct);

    public Task AutosaveAsync(ScanSession session, IReadOnlyList<ScannedCard> cards, CancellationToken ct = default)
        => WriteAsync(session, cards, _dataPaths.ScanSessionRecoveryPath, ct);

    private async Task WriteAsync(ScanSession session, IReadOnlyList<ScannedCard> cards, string filePath, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var manifest = new SessionManifest
            {
                Id = session.Id,
                Name = session.Name,
                CreatedUtc = session.CreatedUtc,
                SavedUtc = DateTime.UtcNow,
                Cards = new List<CardDto>(cards.Count),
            };

            // Write to a temp file first, then atomically move into place so a crash mid-write can't
            // truncate an existing good session (or recovery) file.
            var tempPath = filePath + ".tmp";
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                for (int i = 0; i < cards.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var card = cards[i];
                    string? imageEntry = null;

                    if (!string.IsNullOrEmpty(card.TempImagePath) && File.Exists(card.TempImagePath))
                    {
                        var ext = Path.GetExtension(card.TempImagePath);
                        if (string.IsNullOrEmpty(ext)) ext = ".png";
                        imageEntry = $"{ImagesDir}/{i}{ext}";
                        var entry = zip.CreateEntry(imageEntry, CompressionLevel.Fastest);
                        using var es = entry.Open();
                        using var src = new FileStream(card.TempImagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        await src.CopyToAsync(es, ct);
                    }

                    manifest.Cards.Add(ToDto(card, imageEntry));
                }

                var manifestEntry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
                using var ms = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(ms, manifest, JsonOptions, ct);
            }

            File.Move(tempPath, filePath, overwrite: true);
            _logger.LogInformation("Wrote scan session '{Name}' ({Count} cards) to {Path}", session.Name, cards.Count, filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task<ScanSessionOpenResult> OpenAsync(string filePath, CancellationToken ct = default)
        => ReadAsync(filePath, ct);

    public Task<ScanSessionOpenResult> RecoverAsync(CancellationToken ct = default)
        => ReadAsync(_dataPaths.ScanSessionRecoveryPath, ct);

    private async Task<ScanSessionOpenResult> ReadAsync(string filePath, CancellationToken ct)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Scan session file not found.", filePath);

        Directory.CreateDirectory(_dataPaths.TempScansDirectory);

        SessionManifest? manifest;
        var cards = new List<ScannedCard>();

        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            var manifestEntry = zip.GetEntry(ManifestEntry)
                ?? throw new InvalidDataException("Not a valid scan session: missing session.json.");
            using (var ms = manifestEntry.Open())
                manifest = await JsonSerializer.DeserializeAsync<SessionManifest>(ms, JsonOptions, ct);

            if (manifest is null)
                throw new InvalidDataException("Scan session manifest could not be read.");

            foreach (var dto in manifest.Cards)
            {
                ct.ThrowIfCancellationRequested();

                // Extract this card's image to a fresh temp-scans file so it survives independently of
                // the session zip (and matches how a freshly scanned card is stored on disk).
                string tempImagePath = "";
                if (dto.ImageFile is not null && zip.GetEntry(dto.ImageFile) is { } imgEntry)
                {
                    var ext = Path.GetExtension(dto.ImageFile);
                    if (string.IsNullOrEmpty(ext)) ext = ".png";
                    tempImagePath = Path.Combine(_dataPaths.TempScansDirectory, $"{Guid.NewGuid()}{ext}");
                    using var es = imgEntry.Open();
                    using var os = new FileStream(tempImagePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await es.CopyToAsync(os, ct);
                }

                cards.Add(FromDto(dto, tempImagePath));
            }
        }

        var session = new ScanSession
        {
            Id = manifest.Id,
            Name = manifest.Name,
            CreatedUtc = manifest.CreatedUtc,
            // A recovered/opened session is clean relative to the file it came from.
            FilePath = string.Equals(filePath, _dataPaths.ScanSessionRecoveryPath, StringComparison.OrdinalIgnoreCase) ? null : filePath,
            HasUnsavedChanges = false,
        };

        _logger.LogInformation("Opened scan session '{Name}' ({Count} cards) from {Path}", session.Name, cards.Count, filePath);
        return new ScanSessionOpenResult(session, cards);
    }

    public bool TryGetRecoverable(out DateTime savedUtc)
    {
        savedUtc = default;
        var path = _dataPaths.ScanSessionRecoveryPath;
        if (!File.Exists(path)) return false;
        try
        {
            savedUtc = File.GetLastWriteTimeUtc(path);
            return true;
        }
        catch { return false; }
    }

    public void ClearRecovery()
    {
        try
        {
            var path = _dataPaths.ScanSessionRecoveryPath;
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear scan-session recovery file");
        }
    }

    private static CardDto ToDto(ScannedCard c, string? imageEntry) => new()
    {
        ImageFile = imageEntry,
        Hash = c.Hash,
        ArtHashes = c.ArtHashes,
        ScanEdgeHash = c.ScanEdgeHash,
        Game = c.Game,
        Match = c.Match is null ? null : new MatchDto
        {
            Name = c.Match.Name,
            SetCode = c.Match.SetCode,
            SetName = c.Match.SetName,
            CollectorNumber = c.Match.CollectorNumber,
            Rarity = c.Match.Rarity,
            ImageUri = c.Match.ImageUri,
            GameSpecificId = c.Match.GameSpecificId,
            LocalImagePath = c.Match.LocalImagePath,
            Confidence = c.Match.Confidence,
        },
        Condition = c.Condition,
        IsFoil = c.IsFoil,
        FoilType = c.FoilType,
        PurchasePrice = c.PurchasePrice,
        OverrideContainerId = c.OverrideContainer?.Id,
        OverridePage = c.OverridePage,
        OverrideSlot = c.OverrideSlot,
        OverrideSection = c.OverrideSection,
        FlagReason = c.FlagReason,
        Tags = c.Tags.Count > 0 ? c.Tags.ToList() : null,
        LinkedTradeSessionId = c.LinkedTradeSessionId,
        LinkedTradeLabel = c.LinkedTradeLabel,
    };

    private ScannedCard FromDto(CardDto dto, string tempImagePath)
    {
        CardMatch? match = null;
        if (dto.Match is { } m)
        {
            // Re-resolve the full catalog object so commit-time attribute extraction (color/type) and
            // price display work exactly as for a freshly scanned card. If the card is no longer in the
            // catalog, Source stays null and the stored scalar fields still drive the tile + commit.
            object? source = null;
            if (_gameServices.TryGetValue(dto.Game, out var svc))
            {
                try { source = svc.FindCardById(m.GameSpecificId); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not re-resolve card {Id} while opening session", m.GameSpecificId); }
            }

            match = new CardMatch
            {
                Name = m.Name,
                SetCode = m.SetCode,
                SetName = m.SetName ?? "",
                CollectorNumber = m.CollectorNumber,
                Rarity = m.Rarity ?? "",
                ImageUri = m.ImageUri,
                GameSpecificId = m.GameSpecificId,
                LocalImagePath = m.LocalImagePath,
                Confidence = m.Confidence,
                Source = source!,
            };
        }

        var card = new ScannedCard
        {
            TempImagePath = tempImagePath,
            Hash = dto.Hash,
            ArtHashes = dto.ArtHashes,
            ScanEdgeHash = dto.ScanEdgeHash,
            Game = dto.Game,
            Match = match,
            Condition = dto.Condition,
            IsFoil = dto.IsFoil,
            FoilType = dto.FoilType,
            PurchasePrice = dto.PurchasePrice,
            OverridePage = dto.OverridePage,
            OverrideSlot = dto.OverrideSlot,
            OverrideSection = dto.OverrideSection,
            FlagReason = dto.FlagReason,
            LinkedTradeSessionId = dto.LinkedTradeSessionId,
            LinkedTradeLabel = dto.LinkedTradeLabel,
        };

        if (dto.OverrideContainerId is int cid)
            card.OverrideContainer = _storage.GetAll().FirstOrDefault(s => s.Id == cid);

        if (dto.Tags is { Count: > 0 })
            foreach (var t in dto.Tags) card.Tags.Add(t);

        return card;
    }

    // --- Persisted shapes ---

    private sealed class SessionManifest
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "Untitled";
        public DateTime CreatedUtc { get; set; }
        public DateTime SavedUtc { get; set; }
        public List<CardDto> Cards { get; set; } = [];
    }

    private sealed class CardDto
    {
        public string? ImageFile { get; set; }
        public ulong Hash { get; set; }
        public ulong[]? ArtHashes { get; set; }
        public ulong? ScanEdgeHash { get; set; }
        public CardGame Game { get; set; }
        public MatchDto? Match { get; set; }
        public string Condition { get; set; } = "NM";
        public bool IsFoil { get; set; }
        public string? FoilType { get; set; }
        public decimal? PurchasePrice { get; set; }
        public int? OverrideContainerId { get; set; }
        public int? OverridePage { get; set; }
        public int? OverrideSlot { get; set; }
        public string? OverrideSection { get; set; }
        public FlagReason FlagReason { get; set; }
        public List<string>? Tags { get; set; }
        public int? LinkedTradeSessionId { get; set; }
        public string? LinkedTradeLabel { get; set; }
    }

    private sealed class MatchDto
    {
        public string Name { get; set; } = "";
        public string SetCode { get; set; } = "";
        public string? SetName { get; set; }
        public string CollectorNumber { get; set; } = "";
        public string? Rarity { get; set; }
        public string? ImageUri { get; set; }
        public string GameSpecificId { get; set; } = "";
        public string? LocalImagePath { get; set; }
        public double? Confidence { get; set; }
    }
}
