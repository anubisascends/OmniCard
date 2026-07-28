using OmniCard.Collection;
using Xunit;

namespace OmniCard.Tests.Services;

public class ImportFileClassifierTests
{
    [Theory]
    [InlineData("Game,GameCardId,Name,SetCode")]                         // AppNative marker
    [InlineData("Quantity,Name,Set Name,Number,Printing,Price")]         // TcgPlayer marker
    [InlineData("Count,Name,Edition,Collector Number")]                  // Moxfield marker
    [InlineData("Name,Set code,Foil,Scryfall ID,Purchase price currency")] // Manabox trio
    public void Classify_KnownCsvHeader_ReturnsCsv(string line)
        => Assert.Equal(ImportKind.Csv, ImportFileClassifier.Classify(line));

    [Theory]
    [InlineData("1 Isperia, Supreme Judge (SCD) 4 *E*")]
    [InlineData("1x Sol Ring (SCD) 276")]
    [InlineData("4 Island")]
    public void Classify_DecklistLine_ReturnsDecklist(string line)
        => Assert.Equal(ImportKind.Decklist, ImportFileClassifier.Classify(line));

    [Theory]
    [InlineData("just some random prose")]
    [InlineData("Name,RandomColumn,Other")]   // comma-list but no known marker → not CSV; not a qty line
    public void Classify_Unrecognized_ReturnsUnknown(string line)
        => Assert.Equal(ImportKind.Unknown, ImportFileClassifier.Classify(line));
}
