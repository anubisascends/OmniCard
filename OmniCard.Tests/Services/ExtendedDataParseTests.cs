using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class ExtendedDataParseTests
{
    [Fact]
    public void Parse_ReturnsDisplayNameValuePairs()
    {
        const string json = """
        [{"name":"Number","displayName":"Number","value":"1-001H"},
         {"name":"Element","displayName":"Element","value":"Fire"}]
        """;
        var pairs = ExtendedDataParser.Parse(json);
        Assert.Equal(2, pairs.Count);
        Assert.Equal("Number", pairs[0].Key);
        Assert.Equal("1-001H", pairs[0].Value);
        Assert.Equal("Fire", pairs[1].Value);
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(ExtendedDataParser.Parse(null));
        Assert.Empty(ExtendedDataParser.Parse(""));
    }

    [Fact]
    public void Parse_NonStringLeafValues_DoesNotThrow_TreatsAsEmptyString()
    {
        const string json = """
        [{"name":"Cost","value":6},
         {"name":"Legal","value":true}]
        """;
        var pairs = ExtendedDataParser.Parse(json);
        Assert.Equal(2, pairs.Count);
        Assert.Equal("Cost", pairs[0].Key);
        Assert.Equal("", pairs[0].Value);
        Assert.Equal("Legal", pairs[1].Key);
        Assert.Equal("", pairs[1].Value);
    }
}
