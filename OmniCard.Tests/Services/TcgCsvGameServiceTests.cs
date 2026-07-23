using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class TcgCsvGameServiceTests
{
    private static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    [Fact]
    public void Product_Deserializes_WithExtendedData()
    {
        const string json = """
        {"results":[{"productId":132375,"name":"Auron (Hero)","cleanName":"Auron Hero",
          "imageUrl":"https://cdn/132375_200w.jpg","groupId":1939,
          "url":"https://tcgplayer.com/132375",
          "extendedData":[
            {"name":"Rarity","displayName":"Rarity","value":"Hero"},
            {"name":"Number","displayName":"Number","value":"1-001H"},
            {"name":"CardType","displayName":"Card Type","value":"Forward"}]}],
          "success":true,"errors":[]}
        """;

        var resp = JsonSerializer.Deserialize<TcgCsvProductsResponse>(json, Camel);

        Assert.NotNull(resp);
        var p = Assert.Single(resp!.Results);
        Assert.Equal(132375, p.ProductId);
        Assert.Equal("Auron (Hero)", p.Name);
        Assert.Equal(1939, p.GroupId);
        Assert.Equal(3, p.ExtendedData.Count);
        Assert.Equal("1-001H", p.ExtendedData.Single(e => e.Name == "Number").Value);
    }

    [Fact]
    public void Prices_Deserialize_WithSubTypeNames()
    {
        const string json = """
        {"results":[
          {"productId":1,"marketPrice":1.50,"subTypeName":"Normal"},
          {"productId":1,"marketPrice":3.00,"subTypeName":"Holofoil"}],
          "success":true,"errors":[]}
        """;

        var resp = JsonSerializer.Deserialize<TcgCsvPricesResponse>(json, Camel);

        Assert.Equal(2, resp!.Results.Count);
        Assert.Equal("Holofoil", resp.Results[1].SubTypeName);
        Assert.Equal(3.00m, resp.Results[1].MarketPrice);
    }
}
