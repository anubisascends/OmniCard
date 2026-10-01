using OmniCard.Imaging;
using OmniCard.Shared.Matching;

namespace OmniCard.Tests.Services.Matching;

public class OcrMatchingServiceTests
{
    [Theory]
    [InlineData(500, 700, 0, 35, 21, 375, 49)]   // Modern: 7%, 3%, 75%, 7% of 500x700
    [InlineData(500, 700, 1, 25, 14, 400, 56)]    // Borderless: 5%, 2%, 80%, 8%
    [InlineData(500, 700, 2, 50, 35, 350, 49)]    // Retro: 10%, 5%, 70%, 7%
    public void ToPixelRect_NameRegions_ReturnsCorrectPixels(
        int imgW, int imgH, int regionIndex, int expectedX, int expectedY, int expectedW, int expectedH)
    {
        var region = OcrMatchingService.NameCropRegions[regionIndex];
        var rect = OcrMatchingService.ToPixelRect(region, imgW, imgH);

        Assert.Equal(expectedX, rect.X);
        Assert.Equal(expectedY, rect.Y);
        Assert.Equal(expectedW, rect.Width);
        Assert.Equal(expectedH, rect.Height);
    }

    [Fact]
    public void ToPixelRect_SymbolRegion_ReturnsCorrectPixels()
    {
        var rect = OcrMatchingService.ToPixelRect(OcrMatchingService.SymbolCropRegion, 500, 700);

        Assert.Equal(410, rect.X);  // 82% of 500
        Assert.Equal(301, rect.Y);  // 43% of 700
        Assert.Equal(60, rect.Width);  // 12% of 500
        Assert.Equal(49, rect.Height); // 7% of 700
    }

    [Fact]
    public void ToPixelRect_ClampsToImageBounds()
    {
        // Region that would extend past image edge
        var rect = OcrMatchingService.ToPixelRect((0.95, 0.95, 0.20, 0.20), 100, 100);

        Assert.Equal(95, rect.X);
        Assert.Equal(95, rect.Y);
        Assert.Equal(5, rect.Width);   // Clamped: min(20, 100-95)
        Assert.Equal(5, rect.Height);  // Clamped
    }

    // --- MTG bottom-left (set code + collector number) parser ---
    // Inputs are real (or realistic) OCR reads of the modern MTG corner block; leading zeros are
    // stripped to match how Scryfall stores the collector number.

    [Theory]
    [InlineData("R 0066\nMKC • EN SVETLIN VELINOV", "MKC", "66")]   // standard modern two-line block
    [InlineData("C 0062\nEOC • EN ALLEN PANAKAL", "EOC", "62")]     // observed sample
    [InlineData("025\nSCD • EN JOHANN BODIN", "SCD", "25")]         // rarity on its own, collector first
    [InlineData("066/281 M\nDMU • EN", "DMU", "66")]                // older "{collector}/{total}" format
    [InlineData("M 0004\nBLC EN", "BLC", "4")]                      // star separator dropped by whitelist
    [InlineData("U 0173\nM3C • EN JESPER EJSING", "M3C", "173")]    // digit-bearing set code
    public void TryExtractMtgSetAndNumber_ParsesRealReads(string ocr, string expectedSet, string expectedNumber)
    {
        var ok = OcrMatchingService.TryExtractMtgSetAndNumber(ocr, out var set, out var number);

        Assert.True(ok);
        Assert.Equal(expectedSet, set);
        Assert.Equal(expectedNumber, number);
    }

    [Fact]
    public void TryExtractMtgSetAndNumber_PrefersCollectorOverCopyrightYear()
    {
        // The copyright year can share the corner block; a shorter non-year number must win.
        var ok = OcrMatchingService.TryExtractMtgSetAndNumber("EOC • EN\nTM & © 2024 WIZARDS 100", out var set, out var number);

        Assert.True(ok);
        Assert.Equal("EOC", set);
        Assert.Equal("100", number);
    }

    // Verbatim OCR passes from a real 1000-card MID/SPM audit batch (Audit_Vault_N) that the old
    // "first digit run anywhere" parser got wrong — each resolved to a real but WRONG printing (or none).
    [Theory]
    [InlineData("C0012\nSPM * EN ANIEKAN UD\n4", "SPM", "12")]               // rarity glued to number; old: border-noise "4"
    [InlineData("5 2G CTR A N\n040/277 C\nMID * EN * RYAN PANCO\n4", "MID", "40")] // rules-box noise above; old: "5"
    [InlineData("A 2\n029/277 U\nMID*EN *CTI BALAI", "MID", "29")]          // rules-box noise above; old: "2"
    [InlineData("C0053\nSPM * EN * BEN HARVEY", "SPM", "53")]               // old: no match at all
    [InlineData("455 2\n185/277 U\nMID*EN ZEZHO CHE", "MID", "185")]        // the line above the set code wins
    [InlineData("C 0053\nSPM EN BEN HARVEY", "SPM", "53")]
    public void TryExtractMtgSetAndNumber_ReadsCollectorFromLineAboveSetCode(string ocr, string expectedSet, string expectedNumber)
    {
        var ok = OcrMatchingService.TryExtractMtgSetAndNumber(ocr, out var set, out var number, out var evidence);

        Assert.True(ok);
        Assert.Equal(expectedSet, set);
        Assert.Equal(expectedNumber, number);
        Assert.True(evidence >= OcrMatchingService.MtgEvidencePaddedAboveSet);
    }

    [Theory]
    [InlineData("066/281 M\nDMU • EN", OcrMatchingService.MtgEvidenceFractionAboveSet)]
    [InlineData("R 0066\nMKC • EN SVETLIN VELINOV", OcrMatchingService.MtgEvidencePaddedAboveSet)]
    [InlineData("MID • EN\n040/277", OcrMatchingService.MtgEvidenceFractionElsewhere)]
    [InlineData("2\nSPM EN ANIEKAN U", OcrMatchingService.MtgEvidenceLoose)]   // a lone digit isn't a printed collector
    public void TryExtractMtgSetAndNumber_ReportsEvidence(string ocr, int expectedEvidence)
    {
        Assert.True(OcrMatchingService.TryExtractMtgSetAndNumber(ocr, out _, out _, out var evidence));
        Assert.Equal(expectedEvidence, evidence);
    }

    [Fact]
    public void RankMtgReads_KeepsDisagreeingAnchoredReadsAsAlternates_MostVotesFirst()
    {
        // Real case: two passes misread "155/277" as "185/277"; the lone correct read must survive as an
        // alternate so the catalog lookup can let the image pick it.
        var reads = OcrMatchingService.RankMtgReads([
            ("MID", "185", OcrMatchingService.MtgEvidenceFractionAboveSet),
            ("MID", "185", OcrMatchingService.MtgEvidenceFractionAboveSet),
            ("MID", "155", OcrMatchingService.MtgEvidenceFractionAboveSet),
        ]);

        Assert.Equal([new MtgPrintedIdentity("MID", "185", 2), new MtgPrintedIdentity("MID", "155", 1)], reads);
    }

    [Fact]
    public void RankMtgReads_AnchoredReadOutranksLooseReads_AndLooseAlternatesAreDropped()
    {
        // Loose digit runs are border/rules-box noise: even with more votes they can't beat an anchored
        // read, and they aren't offered as alternates (they'd only drag in random printings).
        var reads = OcrMatchingService.RankMtgReads([
            ("SPM", "4", OcrMatchingService.MtgEvidenceLoose),
            ("SPM", "4", OcrMatchingService.MtgEvidenceLoose),
            ("SPM", "12", OcrMatchingService.MtgEvidencePaddedAboveSet),
        ]);

        Assert.Equal([new MtgPrintedIdentity("SPM", "12", 1)], reads);
    }

    [Fact]
    public void RankMtgReads_LooseReadSurvivesWhenNothingAnchoredWasRead()
    {
        var reads = OcrMatchingService.RankMtgReads([("EOC", "100", OcrMatchingService.MtgEvidenceLoose)]);

        Assert.Equal([new MtgPrintedIdentity("EOC", "100", 1)], reads);
    }

    [Theory]
    // Real reads off Japanese Shadows over Innistrad cards: the narrow "I" beside the bullet is read as
    // a digit or swallowed. The truncated code is kept (the catalog side repairs it) so the language reads.
    [InlineData("017/297 C\nSO* JP  IGR KIERYL", "SO", "17")]
    [InlineData("267/297 C\nSO JP M CHRISTINE C", "SO", "267")]
    [InlineData("040/297 C\nSO1* JP IOHNN STANKO", "SO1", "40")]
    public void TryExtractMtgSetAndNumber_KeepsMisreadJapaneseSetCode_WithItsLanguage(string ocr, string expectedSet, string expectedNumber)
    {
        Assert.True(OcrMatchingService.TryExtractMtgSetAndNumber(ocr, out var set, out var number, out var evidence, out var language));
        Assert.Equal(expectedSet, set);
        Assert.Equal(expectedNumber, number);
        Assert.Equal(OcrMatchingService.MtgEvidenceFractionAboveSet, evidence);
        Assert.Equal("ja", language);
    }

    [Fact]
    public void TryExtractMtgSetAndNumber_PrefersAFullSetCode_OverATruncatedOne()
    {
        Assert.True(OcrMatchingService.TryExtractMtgSetAndNumber("012/297 C\nXX • EN\nSOI • EN", out var set, out _, out _, out _));
        Assert.Equal("SOI", set);
    }

    [Theory]
    [InlineData("SOME RULES TEXT 123")]     // no language marker → no anchored set code
    [InlineData("MKC • EN")]                 // set code but no collector number
    [InlineData("0066")]                     // collector number but no set code
    [InlineData("")]                         // empty
    public void TryExtractMtgSetAndNumber_RejectsIncompleteReads(string ocr)
    {
        Assert.False(OcrMatchingService.TryExtractMtgSetAndNumber(ocr, out _, out _));
    }

    [Fact]
    public void ToPixelRect_MtgCollectorRegion_IsBottomLeftCorner()
    {
        var rect = OcrMatchingService.ToPixelRect(OcrMatchingService.MtgCollectorRegion, 717, 1001);

        Assert.Equal(14, rect.X);    // 2% of 717
        Assert.Equal(912, rect.Y);   // 91.2% of 1001 — spans both bottom-left lines (collector + set)
        Assert.True(rect.Width > 200 && rect.Width < 260);   // ~34% of 717
        Assert.True(rect.Height > 60);                        // tall enough to cover both lines
        Assert.True(rect.Y + rect.Height <= 1001);           // stays on card
    }
}
