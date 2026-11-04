using OmniCard.Shared.Cards;

namespace OmniCard.Tests.Models;

public class CollectorNumberQueryTests
{
    [Theory]
    [InlineData("0019")]
    [InlineData("19")]
    [InlineData("LOB-EN019")]
    [InlineData("OP01-019")]
    [InlineData("1-019H")]
    [InlineData("019/165")]
    [InlineData("19a")]
    public void TryParse_ReadsOneWordWithADigitAsANumber(string text)
        => Assert.True(CollectorNumberQuery.TryParse(text, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Keep Out")]
    [InlineData("bolt")]
    [InlineData("cn:19")]
    [InlineData("-19")]
    [InlineData("1996 World Champion")]
    [InlineData("a123456789012345678901234")] // too long to be a number
    public void TryParse_RejectsNamesAndSearchSyntax(string? text)
        => Assert.False(CollectorNumberQuery.TryParse(text, out _));

    [Theory]
    // Digits only: the card's own number, whatever the game's format and leading zeros.
    [InlineData("0019", "dsk", "19", true)]
    [InlineData("19", "dsk", "19a", true)]
    [InlineData("0019", "sv1", "019/165", true)]
    [InlineData("19", "OP01", "OP01-019", true)]
    [InlineData("019", "1", "1-019H", true)]
    [InlineData("19", "LOB", "LOB-EN019", true)]
    [InlineData("19", "dsk", "119", false)]
    [InlineData("19", "dsk", "190", false)]
    [InlineData("19", "sv1", "190/165", false)]
    [InlineData("19", "sv1", "1/19", false)] // the total isn't the number
    // Text: the number as written, ignoring case and leading zeros, whole or after a separator.
    [InlineData("lob-en019", "LOB", "LOB-EN019", true)]
    [InlineData("EN019", "LOB", "LOB-EN019", true)]
    [InlineData("EN19", "LOB", "LOB-EN019", true)]
    [InlineData("EN019", "LOB", "LOB-EN0190", false)]
    [InlineData("N019", "LOB", "LOB-EN019", false)]
    [InlineData("OP01-019", "OP01", "OP01-019", true)]
    [InlineData("1-019H", "1", "1-019H", true)]
    [InlineData("2-019", "OP2", "2-019R", true)] // typed without the rarity letter
    [InlineData("2-019", "OP2", "2-0190", false)]
    [InlineData("2-019", "OP12", "12-019R", false)]
    [InlineData("019/165", "sv1", "019/165", true)]
    [InlineData("19a", "dsk", "19a", true)]
    [InlineData("19a", "dsk", "19", false)]
    // A set code kept apart from the number (Riftbound OGN, 19), typed as printed.
    [InlineData("OGN-019", "OGN", "19", true)]
    [InlineData("OGN-019/298", "OGN", "19", true)]
    [InlineData("OGN-019", "SFD", "19", false)]
    public void Matches(string typed, string setCode, string collectorNumber, bool expected)
    {
        Assert.True(CollectorNumberQuery.TryParse(typed, out var q));
        Assert.Equal(expected, q.Matches(setCode, collectorNumber));
    }

    [Fact]
    public void CandidateTerms_NumberSearchesWithoutLeadingZerosAndAsTyped()
    {
        Assert.True(CollectorNumberQuery.TryParse("0019", out var q));
        Assert.Equal(["cn:\"19\"", "cn:\"0019\""], q.CandidateTerms());
    }

    [Fact]
    public void CandidateTerms_SetPrefixedNumberAlsoSearchesTheSet()
    {
        Assert.True(CollectorNumberQuery.TryParse("OGN-019", out var q));
        Assert.Equal(["cn:\"OGN-019\"", "set=\"OGN\" cn:\"19\""], q.CandidateTerms());
    }

    [Theory]
    [InlineData("019/165", 19)]
    [InlineData("OP01-019", 19)]
    [InlineData("1-019H", 19)]
    [InlineData("TG19/TG30", 19)]
    [InlineData("★", null)]
    public void NumberPart(string collectorNumber, int? expected)
        => Assert.Equal(expected, CollectorNumberQuery.NumberPart(collectorNumber));
}
