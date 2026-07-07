using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Services;

/// <summary>Exports an AuditReport to a PDF file. Implemented in Task 7.</summary>
public interface IAuditPdfExporter
{
    void Export(AuditReport report, string filePath);
}

/// <summary>Placeholder stub until Task 7 implements the real PDF exporter.</summary>
public sealed class StubAuditPdfExporter : IAuditPdfExporter
{
    public void Export(AuditReport report, string filePath)
    {
        // No-op stub — real implementation in Task 7
    }
}

public interface IAuditService
{
    bool IsAuditActive { get; }
    int? AuditLocationId { get; }
    string? AuditLocationName { get; }
    void StartAudit(int containerId);
    void EndAudit();
    CardMatch? FindScopedMatch(ulong hash, ulong[]? artHashes);
    AuditReport GenerateReport(IEnumerable<ScannedCard> scannedCards);
}

public sealed class AuditService : IAuditService
{
    private readonly IDbContextFactory<CollectionDbContext> _collectionDbFactory;
    private readonly IDbContextFactory<ScryfallDbContext> _scryfallDbFactory;
    private readonly IStorageContainerService _containerService;
    private readonly ILogger<AuditService> _logger;

    // Scoped index built on StartAudit
    private List<(Guid Id, ulong Hash, string Name, string SetCode, string CollectorNumber, string GameCardId)>? _scopedHashIndex;
    private List<(Guid Id, ulong ArtHash)>? _scopedArtHashIndex;

    // Expected cards for report generation
    private List<CollectionCard>? _expectedCards;

    public bool IsAuditActive { get; private set; }
    public int? AuditLocationId { get; private set; }
    public string? AuditLocationName { get; private set; }

    public AuditService(
        IDbContextFactory<CollectionDbContext> collectionDbFactory,
        IDbContextFactory<ScryfallDbContext> scryfallDbFactory,
        IStorageContainerService containerService,
        ILogger<AuditService> logger)
    {
        _collectionDbFactory = collectionDbFactory;
        _scryfallDbFactory = scryfallDbFactory;
        _containerService = containerService;
        _logger = logger;
    }

    public void StartAudit(int containerId)
    {
        using var collCtx = _collectionDbFactory.CreateDbContext();
        var container = collCtx.StorageContainers.FirstOrDefault(c => c.Id == containerId);
        if (container is null)
            throw new InvalidOperationException($"Container {containerId} not found");

        AuditLocationId = containerId;
        AuditLocationName = container.Name;

        // Load expected cards from the location
        _expectedCards = collCtx.Cards
            .AsNoTracking()
            .Where(c => c.ContainerId == containerId)
            .ToList();

        // Get distinct GameCardIds to build scoped hash index
        var gameCardIds = _expectedCards
            .Select(c => c.GameCardId)
            .Distinct()
            .ToHashSet();

        // Build scoped hash index from Scryfall DB (Card model — Id is Guid PK)
        using var scryfallCtx = _scryfallDbFactory.CreateDbContext();
        var scryfallCards = scryfallCtx.Cards
            .AsNoTracking()
            .Where(c => c.ImageHash != null)
            .Select(c => new { c.Id, Hash = c.ImageHash!.Value, c.Name, c.SetCode, c.CollectorNumber, c.ArtHash })
            .AsEnumerable()
            .Where(c => gameCardIds.Contains(c.Id.ToString()))
            .ToList();

        _scopedHashIndex = scryfallCards
            .Select(c => (c.Id, c.Hash, c.Name, c.SetCode, c.CollectorNumber, GameCardId: c.Id.ToString()))
            .ToList();

        _scopedArtHashIndex = scryfallCards
            .Where(c => c.ArtHash.HasValue)
            .Select(c => (c.Id, c.ArtHash!.Value))
            .ToList();

        IsAuditActive = true;
        _logger.LogInformation("Audit started for container {Id} ({Name}): {Expected} expected cards, {Index} hash index entries",
            containerId, container.Name, _expectedCards.Count, _scopedHashIndex.Count);
    }

    public void EndAudit()
    {
        IsAuditActive = false;
        AuditLocationId = null;
        AuditLocationName = null;
        _scopedHashIndex = null;
        _scopedArtHashIndex = null;
        _expectedCards = null;
        _logger.LogInformation("Audit ended");
    }

    public CardMatch? FindScopedMatch(ulong hash, ulong[]? artHashes)
    {
        if (_scopedHashIndex is null || _scopedHashIndex.Count == 0)
            return null;

        const int MaxDistance = 14;
        const int TieZone = 2;

        // Phase 1: Find best pHash distance
        int bestDistance = int.MaxValue;
        foreach (var (_, cardHash, _, _, _, _) in _scopedHashIndex)
        {
            var dist = PerceptualHashService.HammingDistance(hash, cardHash);
            if (dist < bestDistance)
                bestDistance = dist;
        }

        if (bestDistance > MaxDistance)
            return null;

        // Phase 2: Collect tie-zone candidates
        var candidates = new List<(Guid Id, int Distance, string Name, string SetCode, string CollectorNumber, string GameCardId)>();
        foreach (var (id, cardHash, name, setCode, collNum, gameCardId) in _scopedHashIndex)
        {
            var dist = PerceptualHashService.HammingDistance(hash, cardHash);
            if (dist <= bestDistance + TieZone)
                candidates.Add((id, dist, name, setCode, collNum, gameCardId));
        }

        if (candidates.Count == 0)
            return null;

        // Phase 3: Art hash disambiguation (if multiple candidates and art hashes available)
        var bestCandidate = candidates.OrderBy(c => c.Distance).First();

        if (artHashes is not null && _scopedArtHashIndex is { Count: > 0 } && candidates.Count > 1)
        {
            var artLookup = new Dictionary<Guid, ulong>();
            foreach (var (id, artHash) in _scopedArtHashIndex)
                artLookup.TryAdd(id, artHash);

            int bestCombined = int.MaxValue;
            foreach (var candidate in candidates)
            {
                var combined = candidate.Distance;
                if (artLookup.TryGetValue(candidate.Id, out var candidateArtHash))
                {
                    var artDist = artHashes.Min(ah => PerceptualHashService.HammingDistance(ah, candidateArtHash));
                    combined += artDist;
                }
                if (combined < bestCombined)
                {
                    bestCombined = combined;
                    bestCandidate = candidate;
                }
            }
        }

        var confidence = Math.Max(0, (1.0 - (double)bestCandidate.Distance / MaxDistance) * 100);

        return new CardMatch
        {
            Name = bestCandidate.Name,
            SetCode = bestCandidate.SetCode,
            CollectorNumber = bestCandidate.CollectorNumber,
            GameSpecificId = bestCandidate.GameCardId,
            Confidence = confidence,
            Source = new object(), // Placeholder — scoped match doesn't need full card source
        };
    }

    public AuditReport GenerateReport(IEnumerable<ScannedCard> scannedCards)
    {
        if (_expectedCards is null)
            throw new InvalidOperationException("No audit is active");

        var scans = scannedCards.ToList();

        // Build a bag of expected cards (with duplicates for quantity)
        var expectedBag = _expectedCards
            .Select(c => (c.GameCardId, c.Name, c.SetCode, c.SetName, CollectorNumber: c.Number))
            .ToList();

        var matched = new List<AuditReportItem>();
        var extra = new List<AuditReportItem>();

        foreach (var scan in scans)
        {
            if (scan.Match is not null)
            {
                // Try to consume one expected card with the same GameCardId
                var idx = expectedBag.FindIndex(e => e.GameCardId == scan.Match.GameSpecificId);
                if (idx >= 0)
                {
                    var consumed = expectedBag[idx];
                    expectedBag.RemoveAt(idx);
                    matched.Add(new AuditReportItem
                    {
                        Name = consumed.Name,
                        SetCode = consumed.SetCode,
                        SetName = consumed.SetName,
                        CollectorNumber = consumed.CollectorNumber,
                        GameCardId = consumed.GameCardId,
                        Confidence = scan.Match.Confidence,
                    });
                }
                else
                {
                    // Matched a card but it's not expected in this location
                    extra.Add(new AuditReportItem
                    {
                        Name = scan.Match.Name,
                        SetCode = scan.Match.SetCode,
                        SetName = scan.Match.SetName,
                        CollectorNumber = scan.Match.CollectorNumber,
                        GameCardId = scan.Match.GameSpecificId,
                        Confidence = scan.Match.Confidence,
                        ScanImagePath = scan.TempImagePath,
                    });
                }
            }
            else
            {
                // Unmatched scan — extra with no card data
                extra.Add(new AuditReportItem
                {
                    ScanImagePath = scan.TempImagePath,
                });
            }
        }

        // Remaining expected cards are missing
        var missing = expectedBag.Select(e => new AuditReportItem
        {
            Name = e.Name,
            SetCode = e.SetCode,
            SetName = e.SetName,
            CollectorNumber = e.CollectorNumber,
            GameCardId = e.GameCardId,
        }).ToList();

        return new AuditReport
        {
            LocationName = AuditLocationName ?? "Unknown",
            ExpectedCount = _expectedCards.Count,
            ActualCount = scans.Count,
            Matched = matched,
            Missing = missing,
            Extra = extra,
        };
    }
}
