using OmniCard.CardMatching.OldFrame;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Matching;

namespace OmniCard.Tests.Services.Matching;

/// <summary>
/// Old-frame (1993/1997 frame) MTG printing selection. Same-art reprints hash identically, so the printing
/// is chosen from what the scan physically shows. Cases mirror real vintage scans (CardTest batch): the OCR
/// strings are what Tesseract actually produced on them.
/// </summary>
public class OldFramePrintingResolverTests
{
    // ── Title → name ────────────────────────────────────────────────────────────────────────────────

    private static readonly CardNameIndex Names = new([
        "Prodigal Sorcerer", "Mons's Goblin Raiders", "Goblin Raiders", "Sage Owl", "Elvish Archers",
        "Lim-Dûl's Hex", "Delver of Secrets // Insectile Aberration", "Shock", "Raise Dead", "Dead // Gone",
    ]);

    [Theory]
    [InlineData("| Prodigal Sorcerer *&; 8.\"", "Prodigal Sorcerer")] // frame junk around the title
    [InlineData("Mons s Goblin Raiders", "Mons's Goblin Raiders")]   // apostrophe dropped, word split
    [InlineData(") lvish Archers oo", "Elvish Archers")]             // Goudy capital E misread
    [InlineData("Lim-Dul's Hex", "Lim-Dûl's Hex")]                   // diacritics folded
    [InlineData("Delver of Secrets", "Delver of Secrets // Insectile Aberration")] // front face only
    public void BestMatch_FindsTitleInNoisyRead(string read, string expected)
    {
        var match = Names.BestMatch([read]);
        Assert.NotNull(match);
        Assert.Equal(expected, match.Value.Name);
        Assert.True(match.Value.Similarity >= 0.9, $"similarity {match.Value.Similarity:F2}");
    }

    [Fact]
    public void BestMatch_PrefersTheLongerFullTitle_OverAShorterNameInsideIt()
    {
        // "Goblin Raiders" is an exact sub-run of the read, but the whole title is the better evidence.
        var match = Names.BestMatch(["Mons s Goblin Raiders"]);
        Assert.Equal("Mons's Goblin Raiders", match!.Value.Name);
    }

    [Fact]
    public void BestMatch_MisreadFullTitle_BeatsExactShortWordInside()
    {
        // "Dead" (front face of Dead // Gone) is an exact word of the read, but the whole — slightly
        // misread — title explains more of it.
        var match = Names.BestMatch(["Ftaise Dead < Se eee"]);
        Assert.Equal("Raise Dead", match!.Value.Name);
    }

    [Fact]
    public void BestMatch_OneLetterOff_ReportsPartialSimilarity()
    {
        var match = Names.BestMatch(["Hage Owl"]);
        Assert.Equal("Sage Owl", match!.Value.Name);
        Assert.InRange(match.Value.Similarity, 0.85, 0.87);
    }

    [Fact]
    public void BestMatch_BestReadWinsAcrossVariants()
    {
        var match = Names.BestMatch(["Wer Seuss", "Raise Dead - oe ,"]);
        Assert.Equal("Raise Dead", match!.Value.Name);
    }

    // ── Bottom line parsing ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1995", 1995)]
    [InlineData("1904", 1994)]  // 9 read as 0
    [InlineData("19906", 1996)] // doubled glyph
    [InlineData("1906", 1996)]
    public void PlausibleYears_CoversTesseractConfusions(string token, int year)
        => Assert.Contains(year, OldFramePrintingResolver.PlausibleYears(token));

    [Fact]
    public void ParseBottomLine_CopyrightLineAndYear()
    {
        var facts = OldFramePrintingResolver.ParseBottomLine(["Ilus. Christopher Rush 1/1 | c 19005 Wivards of the Coast. Ine. AH rights reserved."], "Christopher Rush");
        Assert.True(facts.HasCopyrightLine);
        Assert.Contains(1995, facts.Years);
        Assert.Null(facts.CollectorNumber); // "1/1" is power/toughness, not a collector number
    }

    [Fact]
    public void ParseBottomLine_CreditWithoutCopyright_MeansNoCopyrightLine_AndSeesIllusMark()
    {
        // Revised: "Illus. © Anson Maddocks", no copyright line.
        var facts = OldFramePrintingResolver.ParseBottomLine([". dilus. c Anson MadG@dockage = a."], "Anson Maddocks");
        Assert.False(facts.HasCopyrightLine);
        Assert.True(facts.IllusMark);
    }

    [Theory]
    [InlineData("lllts. OSandfaEveringham ~~ * le")] // © read as O, glued to both names
    [InlineData(". Tus. C-SandfaEveringham | i")]
    public void ParseBottomLine_GluedMark_Counts(string read)
    {
        var facts = OldFramePrintingResolver.ParseBottomLine([read], "Sandra Everingham");
        Assert.True(facts.IllusMark);
    }

    [Fact]
    public void ParseBottomLine_MarkAbsent_NeedsTwoCleanReads()
    {
        // One read that dropped the © isn't enough to call it absent (OCR loses it more often than not)…
        var one = OldFramePrintingResolver.ParseBottomLine(["~ Hlfus. Jesper Myrfors\" = | *"], "Jesper Myrfors");
        Assert.Null(one.IllusMark);
        // …two are (Unlimited: "Illus. Jesper Myrfors").
        var two = OldFramePrintingResolver.ParseBottomLine(["Illus. Jesper Myrfors", "llus. Jesper Myrfors"], "Jesper Myrfors");
        Assert.False(two.IllusMark);
    }

    [Fact]
    public void ParseCollectorLine_TakesTheMostAgreedFraction_AndAllYears()
    {
        // Real reads off a Chinese M12 card: one pass loses the slash ("18 249"), one garbles the year.
        var line = OldFramePrintingResolver.ParseCollectorLine([
            "-- Steve rrescott j Bie T & c 1993-2011 Wizards of the Coast LLC 18/249",
            "ae Steve Fresco T & c 1993-2011 Wizards of the Coast LILC 18 249 ;",
            ". : WER c 1993-204 Wizards of the Coast LLC 18/249 . -_",
        ]);

        Assert.NotNull(line);
        Assert.Equal(18, line.Value.Number);
        Assert.Equal(249, line.Value.Total);
        Assert.Contains(2011, line.Value.Years);
    }

    [Theory]
    [InlineData("Steve Prescott T & c 1993-2011 Wizards of the Coast LLC")] // no collector printed
    [InlineData("1/1 power and toughness")]                                // a P/T box isn't a total
    public void ParseCollectorLine_NoFraction_IsNull(string read)
    {
        Assert.Null(OldFramePrintingResolver.ParseCollectorLine([read]));
    }

    [Fact]
    public void ParseBottomLine_UnreadableBand_IsUnknown()
    {
        var facts = OldFramePrintingResolver.ParseBottomLine(["SUS) feree Wlerves b/d"], "Jeff A. Menges");
        Assert.Null(facts.HasCopyrightLine);
        Assert.Null(facts.IllusMark);
    }

    [Theory]
    [InlineData("T & c 1993-2001 Wizards of the Coast, Inc. 251/350", 251, true)]
    [InlineData("T & c 2021 Wizards of the Coast 382", 382, false)]
    public void ParseBottomLine_PrintedCollectorNumber(string read, int number, bool hasTotal)
    {
        var facts = OldFramePrintingResolver.ParseBottomLine([read], null);
        Assert.Equal(number, facts.CollectorNumber);
        Assert.Equal(hasTotal, facts.CollectorHasTotal);
    }

    // ── Ranking ─────────────────────────────────────────────────────────────────────────────────────

    private static Card Printing(string set, string num, string border, string released, string setType = "core",
        string artist = "Christopher Rush", string? flavor = null, string frame = "1993") => new()
    {
        Id = Guid.NewGuid(), Name = "Card", SetCode = set, CollectorNumber = num, BorderColor = border,
        ReleasedAt = released, SetType = setType, Artist = artist, FlavorText = flavor, Frame = frame,
    };

    private static PrintingCandidate Same(Card c, int pHash = 4) => new(c, pHash, 6);

    private static string Winner(IReadOnlyList<RankedPrinting> ranked) => ranked[0].Candidate.Card.SetCode + "#" + ranked[0].Candidate.Card.CollectorNumber;

    [Fact]
    public void Rank_BorderAndCopyright_PickFourthOverArabianNights()
    {
        // Nafs Asp: Arabian Nights (black, no copyright line) vs 4th Edition (white, "© 1995").
        var arn = Printing("arn", "52", "black", "1993-12-17", "expansion");
        var fourth = Printing("4ed", "264", "white", "1995-04-01");
        var ev = new MtgPrintEvidence { BorderColor = "white", BottomLineReads = ["Illus. Christopher Rush 1/1 | c 1995 Wizards of the Coast, Inc, All rights reserved."] };

        var ranked = OldFramePrintingResolver.Rank([Same(arn, 0), Same(fourth, 0)], ev, null, currentCardId: arn.Id.ToString());

        Assert.Equal("4ed#264", Winner(ranked));
    }

    [Fact]
    public void Rank_NoCopyrightLineAndIllusMark_PickRevised()
    {
        var unl = Printing("2ed", "121", "white", "1993-12-01", artist: "Jesper Myrfors");
        var rev = Printing("3ed", "122", "white", "1994-04-11", artist: "Jesper Myrfors");
        var fourth = Printing("4ed", "152", "white", "1995-04-01", artist: "Jesper Myrfors");
        var alpha = Printing("lea", "120", "black", "1993-08-05", artist: "Jesper Myrfors");
        var ev = new MtgPrintEvidence { BorderColor = "white", BottomLineReads = [". Ss. Cryesper MYrtOrs*r = |", "~ Hlfus. Jesper Myrfors\" = | *"] };

        var ranked = OldFramePrintingResolver.Rank([Same(unl), Same(rev), Same(fourth), Same(alpha)], ev, null, currentCardId: unl.Id.ToString());

        Assert.Equal("3ed#122", Winner(ranked));
    }

    [Fact]
    public void Rank_NoEvidence_PrefersCommonPrinting_OverArbitraryPHashPick()
    {
        // pHash picked Unlimited only because it has the lowest collector number; with nothing on the card
        // to tell them apart, the far more common Revised printing wins.
        var unl = Printing("2ed", "123", "white", "1993-12-01");
        var rev = Printing("3ed", "124", "white", "1994-04-11");
        var starter = Printing("itp", "22", "white", "1996-12-31", "starter");

        var ranked = OldFramePrintingResolver.Rank([Same(unl), Same(rev), Same(starter)], MtgPrintEvidence.Empty, null, currentCardId: unl.Id.ToString());

        Assert.Equal("3ed#124", Winner(ranked));
    }

    [Fact]
    public void Rank_PrintedCollectorNumber_PinsThePrinting()
    {
        var seventh = Printing("7ed", "251", "white", "2001-04-11", frame: "1997");
        var seventhFoil = Printing("7ed", "251★", "black", "2001-04-11", frame: "1997");
        var ninth = Printing("9ed", "246", "black", "2005-07-29", frame: "2003");
        var ev = new MtgPrintEvidence { BorderColor = "white", BottomLineReads = ["T & c 1993-2001 Wizards of the Coast, Inc. 251/350"] };

        // 9th Edition hashes closest (same art, modern frame) — the printed number and border still win.
        var ranked = OldFramePrintingResolver.Rank([Same(seventh, 6), Same(seventhFoil, 6), Same(ninth, 2)], ev, null, null);

        Assert.Equal("7ed#251", Winner(ranked));
    }

    [Fact]
    public void Rank_DifferentArt_IsNotInTheGroup()
    {
        var match = Printing("fem", "65d", "black", "1994-11-01", "expansion");
        var otherArt = Printing("fem", "65a", "black", "1994-11-01", "expansion");

        var ranked = OldFramePrintingResolver.Rank(
            [new PrintingCandidate(match, 6, 10), new PrintingCandidate(otherArt, 12, 26)], MtgPrintEvidence.Empty, null, null);

        Assert.Single(ranked);
        Assert.Equal("fem#65d", Winner(ranked));
    }

    [Fact]
    public void FlavorVariants_RequestTextBox_ThenFlavorDecides()
    {
        // Alliances a/b: same art, same everything but flavor text.
        var a = Printing("all", "93a", "black", "1996-06-10", "expansion", flavor: "\"Their fury is their greatest weapon.\"\n—Taaveti of Kelsinko, Elvish Hunter");
        var b = Printing("all", "93b", "black", "1996-06-10", "expansion", flavor: "\"Beneath serenity may lie a hidden rage.\"\n—Jaeuhl Carthalion, Juniper Order Advocate");
        var ev = new MtgPrintEvidence { BorderColor = "black" };

        var first = OldFramePrintingResolver.Rank([Same(a), Same(b)], ev, null, a.Id.ToString());
        Assert.True(OldFramePrintingResolver.NeedsTextBox(first));

        var textBox = "Trample, rampage: 2 Cannot be blocked by fewer than three creatures. \"Beneath serenity may lie a hidden rage.\" —Jaeuhl Carthalion, Juniper Order Advocate";
        var second = OldFramePrintingResolver.Rank([Same(a), Same(b)], ev, textBox, a.Id.ToString());
        Assert.Equal("all#93b", Winner(second));
    }

    [Fact]
    public void Rank_WhiteBorder_PicksChroniclesOverTheDark()
    {
        var dark = Printing("drk", "65", "black", "1994-08-01", "expansion");
        var chronicles = Printing("chr", "49", "white", "1995-07-01", "masters");
        var ev = new MtgPrintEvidence { BorderColor = "white" };

        var ranked = OldFramePrintingResolver.Rank([Same(dark), Same(chronicles)], ev, null, dark.Id.ToString());

        Assert.Equal("chr#49", Winner(ranked));
    }
}
