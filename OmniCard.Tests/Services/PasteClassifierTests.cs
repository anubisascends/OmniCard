using OmniCard.Views.Root;

namespace OmniCard.Tests.Services;

public class PasteClassifierTests
{
    [Theory]
    [InlineData("OP15-041")]
    [InlineData("TMT-002")]
    [InlineData("ST01-001")]
    [InlineData("  OP15-041  ")] // trimmed before matching
    [InlineData("EB01-020")]
    public void Classify_CollectorNumberPattern_ReturnsCode(string text)
    {
        Assert.Equal(PasteClassifier.PasteKind.Code, PasteClassifier.Classify(text));
    }

    [Theory]
    [InlineData("Roronoa Zoro")]
    [InlineData("Monkey.D.Luffy")]
    [InlineData("Kelly Funk")]
    [InlineData("Well-Laid Plans")]   // dash but no digits after it
    [InlineData("R2-D2")]              // dash but non-digit after it
    public void Classify_FreeText_ReturnsName(string text)
    {
        Assert.Equal(PasteClassifier.PasteKind.Name, PasteClassifier.Classify(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Classify_EmptyOrWhitespace_ReturnsEmpty(string? text)
    {
        Assert.Equal(PasteClassifier.PasteKind.Empty, PasteClassifier.Classify(text));
    }

    [Fact]
    public void ShouldAssignDirectly_CodeWithExactlyOneResult_True()
    {
        Assert.True(PasteClassifier.ShouldAssignDirectly(PasteClassifier.PasteKind.Code, 1));
    }

    [Fact]
    public void ShouldAssignDirectly_CodeWithZeroResults_False()
        => Assert.False(PasteClassifier.ShouldAssignDirectly(PasteClassifier.PasteKind.Code, 0));

    [Fact]
    public void ShouldAssignDirectly_CodeWithMultipleResults_False()
        => Assert.False(PasteClassifier.ShouldAssignDirectly(PasteClassifier.PasteKind.Code, 2));

    [Fact]
    public void ShouldAssignDirectly_Name_False()
        => Assert.False(PasteClassifier.ShouldAssignDirectly(PasteClassifier.PasteKind.Name, 1));

    [Fact]
    public void ShouldAssignDirectly_Empty_False()
        => Assert.False(PasteClassifier.ShouldAssignDirectly(PasteClassifier.PasteKind.Empty, 1));
}
