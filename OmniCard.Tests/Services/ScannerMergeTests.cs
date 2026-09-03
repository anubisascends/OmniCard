using OmniCard.Models;
using OmniCard.Scanner;
using Xunit;

namespace OmniCard.Tests.Services;

/// <summary>
/// Covers <see cref="ScannerService.MergeScannerLists"/> — the merge of 64-bit (in-process) and
/// 32-bit (helper) TWAIN sources into one origin-tagged list. The load-bearing invariant is that a
/// scanner reachable by both bitnesses (e.g. the Canon, which ships both a 64- and 32-bit driver)
/// stays on its existing in-process path and is never routed to the 32-bit helper.
/// </summary>
public class ScannerMergeTests
{
    [Fact]
    public void Merge_TagsInProcessAndHelperSources_ByOrigin()
    {
        var merged = ScannerService.MergeScannerLists(
            inProcessNames: ["Canon RS40 TWAIN", "EPSON ET-8500 Series"],
            helperNames: ["PaperStream IP fi-8170"]);

        Assert.Equal(3, merged.Count);
        Assert.Equal(ScannerOrigin.InProcess, merged.Single(s => s.Name == "Canon RS40 TWAIN").Origin);
        Assert.Equal(ScannerOrigin.InProcess, merged.Single(s => s.Name == "EPSON ET-8500 Series").Origin);
        Assert.Equal(ScannerOrigin.X86Host, merged.Single(s => s.Name == "PaperStream IP fi-8170").Origin);
    }

    [Fact]
    public void Merge_ScannerWithBothDrivers_StaysInProcess()
    {
        // Canon exposes both a 64-bit and a 32-bit driver, so it appears in BOTH lists. It must
        // resolve to a single in-process entry — the guarantee that the working Canon path is
        // never disturbed by the 32-bit helper.
        var merged = ScannerService.MergeScannerLists(
            inProcessNames: ["Canon RS40 TWAIN"],
            helperNames: ["Canon RS40 TWAIN", "PaperStream IP fi-8170"]);

        var canon = Assert.Single(merged, s => s.Name == "Canon RS40 TWAIN");
        Assert.Equal(ScannerOrigin.InProcess, canon.Origin);
        Assert.Equal(ScannerOrigin.X86Host, merged.Single(s => s.Name == "PaperStream IP fi-8170").Origin);
    }

    [Fact]
    public void Merge_IsCaseInsensitive_OnCollision()
    {
        var merged = ScannerService.MergeScannerLists(
            inProcessNames: ["Canon RS40 TWAIN"],
            helperNames: ["canon rs40 twain"]);

        var only = Assert.Single(merged);
        Assert.Equal("Canon RS40 TWAIN", only.Name); // in-process spelling wins
        Assert.Equal(ScannerOrigin.InProcess, only.Origin);
    }

    [Fact]
    public void Merge_NoInProcessScanners_ReturnsHelperOnly()
    {
        // The fi-870 scenario: a 32-bit-only scanner is invisible to the 64-bit app, so it comes
        // solely from the helper and must still surface.
        var merged = ScannerService.MergeScannerLists(
            inProcessNames: [],
            helperNames: ["PaperStream IP fi-8170"]);

        var only = Assert.Single(merged);
        Assert.Equal("PaperStream IP fi-8170", only.Name);
        Assert.Equal(ScannerOrigin.X86Host, only.Origin);
    }

    [Fact]
    public void Merge_HelperFailedOrAbsent_ReturnsInProcessUnaffected()
    {
        // Helper missing/errored => empty helper list. The 64-bit scanners (incl. Canon) are intact.
        var merged = ScannerService.MergeScannerLists(
            inProcessNames: ["Canon RS40 TWAIN", "EPSON ET-8500 Series"],
            helperNames: []);

        Assert.Equal(2, merged.Count);
        Assert.All(merged, s => Assert.Equal(ScannerOrigin.InProcess, s.Origin));
    }

    [Fact]
    public void Merge_IgnoresBlankNames()
    {
        var merged = ScannerService.MergeScannerLists(
            inProcessNames: ["", "  "],
            helperNames: ["PaperStream IP fi-8170", ""]);

        var only = Assert.Single(merged);
        Assert.Equal("PaperStream IP fi-8170", only.Name);
    }

    [Fact]
    public void Merge_PreservesOrder_InProcessFirst()
    {
        var merged = ScannerService.MergeScannerLists(
            inProcessNames: ["A", "B"],
            helperNames: ["C", "D"]);

        Assert.Equal(["A", "B", "C", "D"], merged.Select(s => s.Name));
    }
}
