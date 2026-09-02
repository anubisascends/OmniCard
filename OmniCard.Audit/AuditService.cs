using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Audit;

public sealed class AuditService : IAuditService
{
    private readonly IDbContextFactory<OmniCardDbContext> _omniDbFactory;
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
        IDbContextFactory<OmniCardDbContext> omniDbFactory,
        IDbContextFactory<ScryfallDbContext> scryfallDbFactory,
        IStorageContainerService containerService,
        ILogger<AuditService> logger)
    {
        _omniDbFactory = omniDbFactory;
        _scryfallDbFactory = scryfallDbFactory;
        _containerService = containerService;
        _logger = logger;
    }

    /// <summary>Loads the owned singles in a location as <see cref="CollectionCard"/> DTOs — the set
    /// of cards a scan or an imported file is audited against. Shared by the scan and file paths.</summary>
    private static List<CollectionCard> LoadExpectedCards(OmniCardDbContext omniCtx, int containerId)
    {
        return omniCtx.Lots
            .AsNoTracking()
            .Include(l => l.Product)
            .Where(l => l.LocationId == containerId && l.Product.Category == ProductCategory.Single)
            .ToList()
            .Select(l => CollectionCardMapper.ToDto(l, l.Product, 0m))
            .ToList();
    }

    public void StartAudit(int containerId)
    {
        using var omniCtx = _omniDbFactory.CreateDbContext();
        var container = omniCtx.StorageContainers.FirstOrDefault(c => c.Id == containerId);
        if (container is null)
            throw new InvalidOperationException($"Container {containerId} not found");

        AuditLocationId = containerId;
        AuditLocationName = container.Name;

        // Load expected cards from the location — project Lots⋈Products (owned singles) into the
        // CollectionCard DTO shape, same as CardService's read facade.
        _expectedCards = LoadExpectedCards(omniCtx, containerId);

        // Get distinct GameCardIds (as Guids) to build scoped hash index — filter server-side
        var gameCardGuids = _expectedCards
            .Select(c => { Guid.TryParse(c.GameCardId, out var g); return g; })
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToList();

        // Build scoped hash index from Scryfall DB (Card model — Id is Guid PK)
        using var scryfallCtx = _scryfallDbFactory.CreateDbContext();
        var scryfallCards = scryfallCtx.Cards
            .AsNoTracking()
            .Where(c => c.ImageHash != null && gameCardGuids.Contains(c.Id))
            .Select(c => new { c.Id, Hash = c.ImageHash!.Value, c.Name, c.SetCode, c.CollectorNumber, c.ArtHash })
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

        // A scanned card contributes one observation. Unmatched scans carry only an image path.
        var observations = scannedCards.Select(scan => new AuditObservation(
            GameCardId: scan.Match?.GameSpecificId,
            Name: scan.Match?.Name,
            SetCode: scan.Match?.SetCode,
            CollectorNumber: scan.Match?.CollectorNumber,
            SetName: scan.Match?.SetName,
            Confidence: scan.Match?.Confidence,
            Condition: scan.Condition,
            IsFoil: scan.IsFoil,
            FoilType: scan.FoilType,
            ScanImagePath: scan.TempImagePath))
            .ToList();

        // Condition/foil discrepancies are noise from a scanner (condition isn't detected), so the
        // scan audit only reports presence — matched / missing / extra.
        return BuildReport(AuditLocationName ?? "Unknown", _expectedCards, observations,
            sourceLabel: "Scanned", detectMismatches: false);
    }

    public AuditReport GenerateFileAuditReport(int containerId, IEnumerable<CollectionCard> importedCards)
    {
        using var omniCtx = _omniDbFactory.CreateDbContext();
        var container = omniCtx.StorageContainers.FirstOrDefault(c => c.Id == containerId)
            ?? throw new InvalidOperationException($"Container {containerId} not found");

        // Self-contained one-shot: load the location's expected cards fresh (independent of any
        // active scan audit) and diff the imported file against them.
        var expected = LoadExpectedCards(omniCtx, containerId);

        var observations = importedCards.Select(c => new AuditObservation(
            GameCardId: string.IsNullOrWhiteSpace(c.GameCardId) ? null : c.GameCardId,
            Name: c.Name,
            SetCode: c.SetCode,
            CollectorNumber: c.Number,
            SetName: c.SetName,
            Confidence: null,
            Condition: c.Condition,
            IsFoil: c.IsFoil,
            FoilType: c.FoilType,
            ScanImagePath: null))
            .ToList();

        _logger.LogInformation("File audit for container {Id} ({Name}): {Expected} expected, {Observed} in file",
            containerId, container.Name, expected.Count, observations.Count);

        // A known-good export carries reliable condition/foil, so flag those discrepancies too.
        return BuildReport(container.Name, expected, observations,
            sourceLabel: "In File", detectMismatches: true);
    }

    /// <summary>One observed card from an audit source (a scan or an imported row), normalized so the
    /// diff logic is source-agnostic.</summary>
    private sealed record AuditObservation(
        string? GameCardId,
        string? Name,
        string? SetCode,
        string? CollectorNumber,
        string? SetName,
        double? Confidence,
        string? Condition,
        bool IsFoil,
        string? FoilType,
        string? ScanImagePath);

    /// <summary>Diffs a set of observed cards against the expected cards of a location, producing the
    /// matched / missing / extra buckets (and, when <paramref name="detectMismatches"/> is set,
    /// condition/foil discrepancies). Each observation consumes at most one expected copy, matched
    /// first by GameCardId and then by (set code + collector number) as a fallback.</summary>
    private static AuditReport BuildReport(
        string locationName,
        List<CollectionCard> expected,
        List<AuditObservation> observations,
        string sourceLabel,
        bool detectMismatches)
    {
        // Working copy — expected cards are removed as they're consumed; the remainder are "missing".
        var expectedBag = expected.ToList();

        var matched = new List<AuditReportItem>();
        var extra = new List<AuditReportItem>();
        var mismatched = new List<AuditReportItem>();

        foreach (var obs in observations)
        {
            var idx = FindExpectedIndex(expectedBag, obs);
            if (idx >= 0)
            {
                var consumed = expectedBag[idx];
                expectedBag.RemoveAt(idx);

                matched.Add(new AuditReportItem
                {
                    Name = consumed.Name,
                    SetCode = consumed.SetCode,
                    SetName = consumed.SetName,
                    CollectorNumber = consumed.Number,
                    GameCardId = consumed.GameCardId,
                    Confidence = obs.Confidence,
                });

                if (detectMismatches)
                {
                    var discrepancy = DescribeDiscrepancy(consumed, obs);
                    if (discrepancy is not null)
                    {
                        mismatched.Add(new AuditReportItem
                        {
                            Name = consumed.Name,
                            SetCode = consumed.SetCode,
                            SetName = consumed.SetName,
                            CollectorNumber = consumed.Number,
                            GameCardId = consumed.GameCardId,
                            Discrepancy = discrepancy,
                        });
                    }
                }
            }
            else
            {
                // Observed but not expected in this location (or an unidentified scan).
                extra.Add(new AuditReportItem
                {
                    Name = obs.Name,
                    SetCode = obs.SetCode,
                    SetName = obs.SetName,
                    CollectorNumber = obs.CollectorNumber,
                    GameCardId = obs.GameCardId,
                    Confidence = obs.Confidence,
                    ScanImagePath = obs.ScanImagePath,
                });
            }
        }

        // Whatever expected copies weren't consumed are missing from the audit source.
        var missing = expectedBag.Select(e => new AuditReportItem
        {
            Name = e.Name,
            SetCode = e.SetCode,
            SetName = e.SetName,
            CollectorNumber = e.Number,
            GameCardId = e.GameCardId,
        }).ToList();

        return new AuditReport
        {
            LocationName = locationName,
            SourceLabel = sourceLabel,
            ExpectedCount = expected.Count,
            ActualCount = observations.Count,
            Matched = matched,
            Missing = missing,
            Extra = extra,
            Mismatched = mismatched,
        };
    }

    /// <summary>Finds the expected copy an observation should consume: first an exact GameCardId
    /// match, then a (set code + collector number) fallback for sources without a resolvable id.
    /// Returns -1 if nothing matches.</summary>
    private static int FindExpectedIndex(List<CollectionCard> bag, AuditObservation obs)
    {
        if (!string.IsNullOrWhiteSpace(obs.GameCardId))
        {
            var byId = bag.FindIndex(e => string.Equals(e.GameCardId, obs.GameCardId, StringComparison.OrdinalIgnoreCase));
            if (byId >= 0)
                return byId;
        }

        if (!string.IsNullOrWhiteSpace(obs.SetCode) && !string.IsNullOrWhiteSpace(obs.CollectorNumber))
        {
            return bag.FindIndex(e =>
                Norm(e.SetCode) == Norm(obs.SetCode) &&
                Norm(e.Number) == Norm(obs.CollectorNumber));
        }

        return -1;
    }

    private static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();

    /// <summary>Describes a condition/foil discrepancy between an expected copy and its matched
    /// observation, or null if they agree. Condition is only compared when the source reports one.</summary>
    private static string? DescribeDiscrepancy(CollectionCard expected, AuditObservation obs)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(obs.Condition) &&
            !string.Equals(expected.Condition, obs.Condition, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"Condition {expected.Condition} → {obs.Condition}");
        }

        if (expected.IsFoil != obs.IsFoil)
        {
            parts.Add($"Foil {(expected.IsFoil ? "foil" : "normal")} → {(obs.IsFoil ? "foil" : "normal")}");
        }
        else if (expected.IsFoil && obs.IsFoil &&
                 !string.IsNullOrWhiteSpace(obs.FoilType) &&
                 !string.IsNullOrWhiteSpace(expected.FoilType) &&
                 !string.Equals(expected.FoilType, obs.FoilType, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"Foil type {expected.FoilType} → {obs.FoilType}");
        }

        return parts.Count > 0 ? string.Join("; ", parts) : null;
    }
}
