using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching.Games;
using OmniCard.Imaging;
using OmniCard.Tests.Fakes;
using Xunit.Abstractions;

namespace OmniCard.Tests.Services.Matching;

/// <summary>
/// Cross-platform parity of the OCR pipeline. <c>TestData/Baseline/ocr-baseline.json</c> holds what the
/// original System.Drawing pre-processing + Tesseract 5.2 read from every test image on Windows (upstream
/// commit e9291b5). The port
/// changes the pixels Tesseract sees only by GDI+'s rounding noise, but on Linux the engine itself is the
/// distribution's Tesseract 5.x, so raw text is allowed to drift. The contract:
/// <list type="bullet">
/// <item>Every read the original got right (checked against the ground truth in the file names / rendered
/// text) must still be right.</item>
/// <item>Pixel-geometry results (card bounds, border colour, The List glyph) must match exactly.</item>
/// <item>Across all OCR fields, at least <see cref="MinFieldAgreement"/> must equal the original's output.</item>
/// </list>
/// </summary>
public class OcrBaselineTests(ITestOutputHelper output)
{
    // Measured: 75.3% on Windows (same Tesseract 5.2 binaries as the baseline) and 66.4% on Ubuntu 24.04
    // (distribution Tesseract 5.3.4). The differences are raw multi-pass text (title/credit/text-box reads,
    // noise tokens); no read that matched the ground truth was lost on either OS.
    internal const double MinFieldAgreement = 0.6;

    private sealed record MtgRead(string SetCode, string CollectorNumber, int Votes, string? Language);

    private sealed record Entry(
        string file, string? name, string? optcg, string[] optcgTexts, string? riftbound, string? pokemon,
        string? yugioh, string? fftcg, MtgRead[] mtgReads, bool listPresent, double listPeak, string? yugiohEdition,
        string[] titleReads, string? borderColor, string[] bottomLineReads, string? textBox, int[] cardBounds,
        string? classifiedBorder);

    private static readonly Regex ScanName = new(@"\[(?<set>[A-Z0-9]+)\] #(?<num>\d+)\.png$");
    private static readonly Regex ArtName = new(@"^art/art/(?<set>[a-z0-9]+)/(?<num>\d+)\.jpg$");

    // Printed (set, collector) identity known from the fixture's file name, if any.
    private static string? MtgTruth(string file)
    {
        var m = ScanName.Match(file);
        if (!m.Success) m = ArtName.Match(file);
        return m.Success ? $"{m.Groups["set"].Value.ToUpperInvariant()}#{m.Groups["num"].Value}" : null;
    }

    private static readonly Dictionary<string, (string Field, string Value)> RenderedTruth = new()
    {
        ["Baseline/OcrRendered/optcg-OP15-043.png"] = ("optcg", "OP15-043"),
        ["Baseline/OcrRendered/fftcg-29-048C.png"] = ("fftcg", "29-048C"),
        ["Baseline/OcrRendered/mtg-66-MKC.png"] = ("mtg", "MKC#66"),
        ["Baseline/OcrRendered/yugioh-1st-edition.png"] = ("edition", "1st Edition"),
        ["Baseline/OcrRendered/yugioh-DAMA-EN012.png"] = ("yugioh", "DAMA-EN012"),
    };

    [Fact]
    public async Task OcrResults_MatchGdiPlusBaseline()
    {
        using var ocr = new OcrMatchingService(new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            NullLogger<OcrMatchingService>.Instance);
        int fields = 0, agreed = 0;
        var mismatches = new List<string>();
        var regressions = new List<string>();

        foreach (var e in ImagingBaselineTests.LoadBaseline<Entry>("ocr-baseline.json"))
        {
            var b = File.ReadAllBytes(TestImages.Fixture(e.file.Split('/')));
            var actual = new Dictionary<string, string?>
            {
                ["name"] = (await ocr.AnalyzeCardAsync(b)).RecognizedName,
                ["optcg"] = (await ocr.DetectOptcgCollectorNumberAsync(b)).CollectorNumber,
                ["optcgTexts"] = string.Join(" | ", await ocr.ReadOptcgCollectorTextsAsync(b)),
                ["riftbound"] = (await ocr.DetectRiftboundCollectorNumberAsync(b)).CollectorNumber,
                ["pokemon"] = (await ocr.DetectCollectorNumberAsync(b, PokemonService.OcrSpec)).CollectorNumber,
                ["yugioh"] = (await ocr.DetectCollectorNumberAsync(b, YugiohService.OcrSpec)).CollectorNumber,
                ["fftcg"] = (await ocr.DetectCollectorNumberAsync(b, FinalFantasyService.OcrSpec)).CollectorNumber,
                ["mtg"] = (await ocr.DetectMtgSetAndNumberCandidatesAsync(b)).Reads.Select(r => $"{r.SetCode}#{r.CollectorNumber}").FirstOrDefault(),
                ["edition"] = (await ocr.DetectYugiohEditionAsync(b)).Edition,
                ["textBox"] = await ocr.ReadMtgTextBoxAsync(b),
            };
            var evidence = await ocr.ReadMtgPrintEvidenceAsync(b);
            actual["titleReads"] = string.Join(" | ", evidence.TitleReads);
            actual["bottomLineReads"] = string.Join(" | ", evidence.BottomLineReads);
            var list = await ocr.DetectMtgListSymbolAsync(b);

            var expected = new Dictionary<string, string?>
            {
                ["name"] = e.name, ["optcg"] = e.optcg, ["optcgTexts"] = string.Join(" | ", e.optcgTexts),
                ["riftbound"] = e.riftbound, ["pokemon"] = e.pokemon, ["yugioh"] = e.yugioh, ["fftcg"] = e.fftcg,
                ["mtg"] = e.mtgReads.Select(r => $"{r.SetCode}#{r.CollectorNumber}").FirstOrDefault(),
                ["edition"] = e.yugiohEdition, ["textBox"] = e.textBox,
                ["titleReads"] = string.Join(" | ", e.titleReads), ["bottomLineReads"] = string.Join(" | ", e.bottomLineReads),
            };
            foreach (var (field, value) in expected)
            {
                fields++;
                if (value == actual[field]) agreed++;
                else mismatches.Add($"{e.file} {field}: '{value}' -> '{actual[field]}'");
            }

            // Ground truth: a read the original got right must stay right.
            var truth = MtgTruth(e.file);
            if (truth is not null && expected["mtg"] == truth && actual["mtg"] != truth)
                regressions.Add($"{e.file}: MTG set/number {truth} now read as '{actual["mtg"]}'");
            if (RenderedTruth.TryGetValue(e.file, out var rendered) && expected[rendered.Field] == rendered.Value
                && actual[rendered.Field] != rendered.Value)
                regressions.Add($"{e.file}: {rendered.Field} {rendered.Value} now read as '{actual[rendered.Field]}'");

            // Pixel geometry — no OCR involved, must be exact.
            using var bmp = ImageOps.Decode(b);
            var bounds = OcrMatchingService.FindCardBounds(bmp);
            Assert.Equal(e.cardBounds, new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height });
            Assert.Equal(e.classifiedBorder, OcrMatchingService.ClassifyBorder(bmp));
            Assert.Equal(e.borderColor, evidence.BorderColor);
            Assert.True(e.listPresent == list.Present, $"{e.file}: List glyph {e.listPresent} -> {list.Present} (peak {e.listPeak:F3} -> {list.Confidence:F3})");
            Assert.True(Math.Abs(e.listPeak - list.Confidence) < 0.05, $"{e.file}: List glyph peak {e.listPeak:F3} -> {list.Confidence:F3}");
        }

        double agreement = (double)agreed / fields;
        output.WriteLine($"OCR field agreement with the GDI+ baseline: {agreed}/{fields} = {agreement:P1}");
        foreach (var m in mismatches) output.WriteLine("  " + m);

        Assert.True(regressions.Count == 0, "OCR regressions:\n" + string.Join("\n", regressions));
        Assert.True(agreement >= MinFieldAgreement, $"OCR field agreement {agreement:P1} is below {MinFieldAgreement:P0}");
    }
}
