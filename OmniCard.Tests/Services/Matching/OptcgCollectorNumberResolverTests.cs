using OmniCard.CardMatching.Games;

namespace OmniCard.Tests.Services.Matching;

/// <summary>
/// The resolver snaps Tesseract's reads of the One Piece collector line to catalog numbers. Every read
/// below is a real garble captured from a 780-scan batch (Audit_Vault_AG), where the strict
/// "OP15-043" pattern matched only 131 of 780 scans.
/// </summary>
public class OptcgCollectorNumberResolverTests
{
    private static readonly OptcgCollectorNumberResolver Resolver = new(
    [
        "OP14-107", "OP14-109", "OP14-101", "OP14-115", "OP14-108", "EB03-054", "EB03-056", "EB03-050",
        "EB04-055", "OP09-055", "OP09-050", "OP09-110", "OP11-088", "OP05-110", "ST13-019",
        "P-141", "P-110", "P-011", "P-091",
    ]);

    private static string? Top(params string[] reads) => Resolver.Resolve(reads).FirstOrDefault()?.CardNumber;

    [Theory]
    [InlineData("ST13-019", "ST13-019")]   // clean read
    [InlineData("0P14-109", "OP14-109")]   // letter O read as zero
    [InlineData("EBO3-054", "EB03-054")]   // zero read as letter O
    [InlineData("OP14-1070AO", "OP14-107")] // rarity glyph tacked on after the number
    [InlineData("P14-115O", "OP14-115")]   // leading round "O" dropped
    [InlineData("EBO04-055", "EB04-055")]  // one zero read as two glyphs
    [InlineData("OP0O9-055", "OP09-055")]
    [InlineData("SN  OP09-1100-", "OP09-110")] // subtype junk in front of the number
    public void Resolve_SnapsGarbledRead(string read, string expected) => Assert.Equal(expected, Top(read));

    [Fact]
    public void Resolve_FullNumberOneEditOff_BeatsExactShortPromoInsideIt()
    {
        // "P14-115O" contains the promo key "P141" verbatim; the full OP14-115 (one dropped "O") must win.
        Assert.Equal("OP14-115", Top("P14-115O"));
        Assert.Equal("OP14-109", Top("P14-1091"));
    }

    [Fact]
    public void Resolve_ShortPromoKey_MatchesOnlyExactly()
    {
        Assert.Equal("P-110", Top("P-110"));
        Assert.Null(Top("P-112"));
    }

    [Theory]
    [InlineData("KP")]
    [InlineData("W")]
    [InlineData("GERMA66")]
    [InlineData("")]
    public void Resolve_NoNumberInRead_ReturnsNothing(string read) => Assert.Empty(Resolver.Resolve([read]));

    [Fact]
    public void Resolve_VotesAcrossReads_ExactBeatsFuzzy()
    {
        // Tesseract dropped "09" from OP09-110 on two passes — an exact hit on the wrong OP11-088 — and read
        // it right on a third. The vote ranks the double misread first; OptcgService lets the image overrule.
        var candidates = Resolver.Resolve(["OP-11088", "OP-11088", "OPO9-110"]);

        Assert.Equal(["OP11-088", "OP09-110"], candidates.Select(c => c.CardNumber).Take(2));
        Assert.Equal(2, candidates[0].ExactVotes);
        Assert.Equal(1, candidates[1].ExactVotes);
    }

    [Fact]
    public void Resolve_OneEditRead_CountsNoExactVote()
    {
        var candidate = Assert.Single(Resolver.Resolve(["P14-115O"]));
        Assert.Equal("OP14-115", candidate.CardNumber);
        Assert.Equal(0, candidate.ExactVotes);
    }

    [Theory]
    [InlineData("0P14109", "XX0P14109YY", 0)]
    [InlineData("0P14109", "P141090", 1)]
    [InlineData("0P14109", "0P14108", 1)]
    [InlineData("0P14109", "0P1", 4)]
    public void WindowedDistance_IgnoresSurroundingText(string pattern, string text, int expected)
        => Assert.Equal(expected, OptcgCollectorNumberResolver.WindowedDistance(pattern, text));

    [Fact]
    public void Key_FoldsLookAlikeGlyphs()
        => Assert.Equal(OptcgCollectorNumberResolver.Key("EB03-054"), OptcgCollectorNumberResolver.Key("EBO3 054"));
}
