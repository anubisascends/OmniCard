using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection.Settings;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Scanning;
using OmniCard.Shared.Settings;
using OmniCard.Web.Services.ScanBatches;
using Xunit;

namespace OmniCard.Tests.Web.ScanBatches;

/// <summary>
/// Runs only where a folder of real re-saved scans exists (skipped in CI). Each sample is a
/// <c>_processed\&lt;batch&gt;</c> folder from a run the capture software saved more than once:
/// <c>0001.png</c> plus its byte-identical re-saves <c>0001 (2).png</c>, <c>0001 (3).png</c>…
/// </summary>
/// <remarks>The root defaults to <c>F:\Scans\Magic\_processed</c>; set <c>OMNICARD_SCAN_SAMPLES</c> to
/// point elsewhere. Only the first <see cref="DefaultSampleSize"/> cards are replayed unless
/// <c>OMNICARD_SCAN_SAMPLES_ALL=1</c>.</remarks>
public sealed class ScanDuplicateSampleTests : IDisposable
{
    private const int DefaultSampleSize = 40;

    internal static string SampleRoot =>
        Environment.GetEnvironmentVariable("OMNICARD_SCAN_SAMPLES") is { Length: > 0 } root
            ? root
            : @"F:\Scans\Magic\_processed";

    private readonly ScanBatchFixture _f = new();

    public void Dispose() => _f.Dispose();

    [ScanSampleFact("SortingTray_White")]
    public void SortingTray_White_ResavedOnce() => Replay("SortingTray_White", expectedWaves: 2);

    [ScanSampleFact("Unstable")]
    public void Unstable_ResavedTwice() => Replay("Unstable", expectedWaves: 3);

    private static readonly Regex Copy = new(@"^(?<base>.+?)(?: \((?<n>\d+)\))?(?<ext>\.[^.]+)$");

    /// <summary>
    /// Replay the sample the way it arrived: each wave (the originals, then every re-save) is dropped
    /// into the watched folder under the original names and picked up. Every original must become a
    /// card to review, every re-save a duplicate of it, and each wave one repeated run.
    /// </summary>
    private void Replay(string folder, int expectedWaves)
    {
        var source = Path.Combine(SampleRoot, folder);
        var parsed = Directory.GetFiles(source)
            .Select(f => (File: f, M: Copy.Match(Path.GetFileName(f))))
            .Where(x => x.M.Success)
            .Select(x => (x.File, Name: x.M.Groups["base"].Value + x.M.Groups["ext"].Value,
                Wave: x.M.Groups["n"].Success ? int.Parse(x.M.Groups["n"].Value) : 1))
            .ToList();
        var all = Environment.GetEnvironmentVariable("OMNICARD_SCAN_SAMPLES_ALL") == "1";
        var names = parsed.Where(p => p.Wave == 1).Select(p => p.Name).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (!all) names = names.Take(DefaultSampleSize).ToList();
        var chosen = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var waves = parsed.Where(p => chosen.Contains(p.Name)).GroupBy(p => p.Wave).OrderBy(g => g.Key).ToList();
        Assert.Equal(expectedWaves, waves.Count);

        var mtg = Path.Combine(_f.ScanRoot, "Mtg");
        var batchFolder = Path.Combine(mtg, folder);
        Directory.CreateDirectory(batchFolder);
        var ingestor = new ScanFolderIngestor(_f.Factory, _f.Storage, _f.Clock, NullLogger<ScanFolderIngestor>.Instance);
        var settings = new ScanFolderSettings { Enabled = true, Folders = [new() { Game = CardGame.Mtg, Path = mtg }] };

        foreach (var wave in waves)
        {
            Assert.Equal(names.Count, wave.Count());
            foreach (var (file, name, _) in wave)
                File.Copy(file, Path.Combine(batchFolder, name));
            ingestor.RunOnce(settings); // noticed; left alone while it waits
            _f.Clock.Advance(TimeSpan.FromSeconds(settings.QuietPeriodSeconds + 1));
            ingestor.RunOnce(settings); // quiet period over: taken in
            Assert.Empty(Directory.GetFiles(batchFolder)); // everything picked up
        }

        // Each wave is its own batch (later files never join a started batch).
        using var db = _f.Db();
        var batches = db.ScanBatches.OrderBy(b => b.Id).ToList();
        Assert.Equal(expectedWaves, batches.Count);
        var first = batches[0];
        var items = db.ScanBatchItems.OrderBy(i => i.ScanBatchId).ThenBy(i => i.Sequence).ToList();
        var originals = items.Where(i => i.State == ScanBatchItemState.Open).ToList();
        var copies = items.Where(i => i.State == ScanBatchItemState.Duplicate).ToList();

        // Every real scan is kept, in the first batch, and no two of them share bytes.
        Assert.Equal(names, originals.Select(i => i.OriginalFileName));
        Assert.All(originals, i => Assert.Equal(first.Id, i.ScanBatchId));
        Assert.Equal(originals.Count, originals.Select(i => i.ContentHash).Distinct().Count());

        // Every re-save is set aside as a copy of the scan with the same name.
        Assert.Equal(names.Count * (expectedWaves - 1), copies.Count);
        var byId = originals.ToDictionary(i => i.Id);
        Assert.All(copies, c => Assert.Equal(c.OriginalFileName, byId[c.DuplicateOfItemId!.Value].OriginalFileName));

        // Each re-saved wave is closed straight away and reads as one repeated run of the whole sample.
        var service = new ScanBatchService(_f.Factory, null!, _f.Storage, new ScanFolderSettingsService(_f.Paths),
            _f.Clock, NullLogger<ScanBatchService>.Instance);
        Assert.Equal(names.Count, service.Get(first.Id, userId: null).Summary.Total);
        foreach (var later in batches.Skip(1))
        {
            Assert.Equal(ScanBatchStatus.Discarded, later.Status);
            var dto = service.Get(later.Id, userId: null);
            Assert.Equal(names.Count, dto.Summary.Duplicates);
            var run = Assert.Single(dto.DuplicateRuns);
            Assert.Equal(names.Count, run.Count);
            Assert.Equal((names[0], names[^1]), (run.FirstFile, run.LastFile));
            Assert.Equal((names[0], names[^1]), (run.OriginalFirstFile, run.OriginalLastFile));
            Assert.Equal(first.Name, run.OriginalBatchName);
        }
    }
}

/// <summary>A fact that's skipped unless the named sample folder exists under
/// <see cref="ScanDuplicateSampleTests.SampleRoot"/>.</summary>
internal sealed class ScanSampleFactAttribute : FactAttribute
{
    public ScanSampleFactAttribute(string folder)
    {
        if (!Directory.Exists(Path.Combine(ScanDuplicateSampleTests.SampleRoot, folder)))
            Skip = $"Sample scans '{folder}' not found under {ScanDuplicateSampleTests.SampleRoot}.";
    }
}
