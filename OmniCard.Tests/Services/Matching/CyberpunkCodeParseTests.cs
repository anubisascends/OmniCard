using OmniCard.Imaging;
using OmniCard.Shared.Matching;

namespace OmniCard.Tests.Services.Matching;

/// <summary>Parsing of the raw Tesseract reads of a Cyberpunk TCG card's product line and collector number.
/// The product-line inputs are reads actually produced on the sample scans (brackets read as I/J/L, 0 as O
/// or OQ, a missing hyphen).</summary>
public class CyberpunkCodeParseTests
{
    [Theory]
    [InlineData("MSO1-WNC[A]")]
    [InlineData("MSO1-WNCIA]")]
    [InlineData("MSO1-WNCIAL")]
    [InlineData("MSO1-WNCI[A]")]
    [InlineData("MSO1WNC[A]")]
    [InlineData("MSO1WNCIA")]
    [InlineData("MSOQ1-WNC[A]")]
    [InlineData("MSO1-WNC[A")]
    [InlineData("MS01 - WNC [A]")]
    public void ParseProduct_ReadsProductSetAndVariant(string read)
    {
        var parsed = CyberpunkCodeReader.ParseProduct(read);

        Assert.NotNull(parsed);
        Assert.Equal(("MS01", "WNC", "A"), (parsed.Value.Product, parsed.Value.SetCode, parsed.Value.Variant));
    }

    [Theory]
    [InlineData("MSO1-WNC")]
    [InlineData("MS01WNC")]
    public void ParseProduct_NoVariant_KeepsTheSetCode(string read)
    {
        var parsed = CyberpunkCodeReader.ParseProduct(read);

        Assert.NotNull(parsed);
        Assert.Equal("WNC", parsed.Value.SetCode);
        Assert.Null(parsed.Value.Variant);
    }

    [Theory]
    [InlineData("")]
    [InlineData("WNC")]
    [InlineData("1393")]
    public void ParseProduct_Garbage_IsNull(string read) => Assert.Null(CyberpunkCodeReader.ParseProduct(read));

    [Theory]
    [InlineData("120", "120", false)]
    [InlineData(" 1 2 0 ", "120", false)]
    [InlineData("B120", "120", true)]    // β read as B
    [InlineData("8120", "120", true)]    // β read as 8: no printed number has four digits
    public void ParseNumber(string read, string number, bool beta)
    {
        var parsed = CyberpunkCodeReader.ParseNumber(read);

        Assert.NotNull(parsed);
        Assert.Equal((number, beta), (parsed.Value.Number, parsed.Value.Beta));
    }

    [Theory]
    [InlineData("4")]
    [InlineData("14")]
    [InlineData("1393")]   // a stray fourth digit that isn't a β
    [InlineData("")]
    public void ParseNumber_NotThreeDigits_IsNull(string read) => Assert.Null(CyberpunkCodeReader.ParseNumber(read));

    [Fact]
    public void CatalogNumber_PrefixesBetaWithB()
    {
        Assert.Equal("120", new CyberpunkPrintedCode("MS01", "WNC", "A", "120", false).CatalogNumber);
        Assert.Equal("B120", new CyberpunkPrintedCode("MS01", "WNC", "A", "120", true).CatalogNumber);
        Assert.Null(CyberpunkPrintedCode.Empty.CatalogNumber);
    }
}
