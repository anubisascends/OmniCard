using OmniCard.CardMatching.Games;
using OmniCard.Collection.Settings;
using OmniCard.eBay;
using OmniCard.Imaging;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;

namespace OmniCard.Tests.Models;

/// <summary>The shared language vocabulary, the per-game download selection, and the printed-language
/// parsers (Yu-Gi-Oh! region codes, the MTG "• JP" marker).</summary>
public class CardLanguagesTests
{
    [Theory]
    [InlineData("ja", "ja")]
    [InlineData("JP", "ja")]
    [InlineData("Japanese", "ja")]
    [InlineData("SP", "es")]
    [InlineData("KR", "ko")]
    [InlineData("CS", "zhs")]
    [InlineData("CT", "zht")]
    [InlineData("zh-TW", "zht")]
    [InlineData("English", "en")]
    [InlineData("  de ", "de")]
    [InlineData("klingon", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_MapsPrintedAndIsoSpellings(string? input, string? expected) =>
        Assert.Equal(expected, CardLanguages.Normalize(input));

    [Theory]
    [InlineData("en", null)]
    [InlineData("English", null)]
    [InlineData("ja", "ja")]
    [InlineData("bogus", null)]
    public void ToStored_EnglishAndUnknownAreNull(string input, string? expected) =>
        Assert.Equal(expected, CardLanguages.ToStored(input));

    [Fact]
    public void SanitizeCatalogSelection_AlwaysEnglish_OnlyDownloadable_InCanonicalOrder()
    {
        Assert.Equal(["en", "ja", "fr"], CardLanguages.SanitizeCatalogSelection(CardGame.OnePiece, ["fr", "JP", "de", "fr"]));
        Assert.Equal(["en"], CardLanguages.SanitizeCatalogSelection(CardGame.YuGiOh, ["ja", "de"]));
        Assert.Equal(["en"], CardLanguages.SanitizeCatalogSelection(CardGame.Pokemon, null));
    }

    [Fact]
    public void ForGame_ListsPrintedLanguages_EnglishFirst()
    {
        foreach (var game in Enum.GetValues<CardGame>())
        {
            Assert.Equal("en", CardLanguages.ForGame(game)[0]);
            Assert.Equal("en", CardLanguages.DownloadableFor(game)[0]);
            Assert.All(CardLanguages.DownloadableFor(game), l => Assert.Contains(l, CardLanguages.ForGame(game)));
        }
        Assert.Contains("ja", CardLanguages.ForGame(CardGame.FinalFantasy));
    }

    [Fact]
    public void CatalogLanguageSettings_PersistPerGame_AndSanitize()
    {
        var dir = Path.Combine(Path.GetTempPath(), "catlang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dataPath = new Moq.Mock<OmniCard.Shared.Settings.IDataPathService>();
            dataPath.Setup(d => d.DataDirectory).Returns(dir);
            var svc = new CatalogLanguageSettingsService(dataPath.Object);
            Assert.Equal(["en"], svc.GetLanguages(CardGame.Mtg));

            Assert.Equal(["en", "ja", "de"], svc.SetLanguages(CardGame.Mtg, ["de", "jp"]));
            svc.SetLanguages(CardGame.YuGiOh, ["ja"]);

            var reloaded = new CatalogLanguageSettingsService(dataPath.Object);
            Assert.Equal(["en", "ja", "de"], reloaded.GetLanguages(CardGame.Mtg));
            Assert.Equal(["en"], reloaded.GetLanguages(CardGame.YuGiOh));
            Assert.Equal(["en"], reloaded.GetLanguages(CardGame.OnePiece));

            File.WriteAllText(Path.Combine(dir, "catalog-languages.json"), "{ not json");
            Assert.Equal(["en"], reloaded.GetLanguages(CardGame.Mtg));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("RA05-DE085", "RA05-EN085", "de")]
    [InlineData("RA05-SP085", "RA05-EN085", "es")]
    [InlineData("PHNI-JP001", "PHNI-EN001", "ja")]
    [InlineData("LPST-KR003", "LPST-EN003", "ko")]
    [InlineData("QCAC-SC024", "QCAC-EN024", "zhs")]
    [InlineData("LOB-G001", "LOB-E001", "de")]
    [InlineData("RA05-EN085", "RA05-EN085", "en")]
    [InlineData("SDRB-AE001", "SDRB-AE001", "en")]
    [InlineData("LOB-001", "LOB-001", null)]
    [InlineData("DAMA-ENULZ", "DAMA-ENULZ", null)]
    public void Yugioh_NormalizeRegionCode(string read, string expected, string? language)
    {
        Assert.Equal(expected, YugiohService.NormalizeRegionCode(read, out var lang));
        Assert.Equal(language, lang);
    }

    [Theory]
    [InlineData("040/277 R\nMKC • JP", "MKC", "ja")]
    [InlineData("040/277 R\nMKC • SP", "MKC", "es")]
    [InlineData("040/277 R\nNEO • EN", "NEO", "en")]
    [InlineData("0066 M\nDMU•CS", "DMU", "zhs")]
    public void Mtg_SetLineLanguageMarker_IsParsed(string ocrText, string expectedSet, string expectedLanguage)
    {
        Assert.True(OcrMatchingService.TryExtractMtgSetAndNumber(ocrText, out var set, out _, out _, out var language));
        Assert.Equal(expectedSet, set);
        Assert.Equal(expectedLanguage, language);
    }

    [Fact]
    public void Mtg_RankReads_CarriesMajorityLanguage()
    {
        var reads = OcrMatchingService.RankMtgReads([
            ("NEO", "1", OcrMatchingService.MtgEvidenceFractionAboveSet, "ja"),
            ("NEO", "1", OcrMatchingService.MtgEvidenceFractionAboveSet, "ja"),
            ("NEO", "1", OcrMatchingService.MtgEvidenceFractionAboveSet, null),
        ]);

        Assert.Equal([new OmniCard.Shared.Matching.MtgPrintedIdentity("NEO", "1", 3, "ja")], reads);
    }

    [Theory]
    [InlineData(null, "English")]
    [InlineData("en", "English")]
    [InlineData("ja", "Japanese")]
    [InlineData("zht", "Chinese")]
    public void Ebay_LanguageAspect(string? language, string expected) =>
        Assert.Equal(expected, EbayListingService.EbayLanguageAspect(language));
}
