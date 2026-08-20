using System.Net;
using OmniCard.Collection;

namespace OmniCard.Tests.Services;

public class UpcLookupServiceTests
{
    private static UpcLookupService Service(HttpStatusCode status, string body)
        => new(new FakeHttpClientFactory(new FakeHttpHandler(status, body)));

    [Fact]
    public async Task LookupAsync_ParsesFirstItem()
    {
        const string body = """
        {
          "code": "OK",
          "total": 1,
          "items": [
            {
              "title": "Pokemon Scarlet & Violet Booster Box",
              "brand": "Pokemon",
              "description": "36 booster packs",
              "category": "Toys & Games > Trading Cards",
              "images": [ "https://img.example/box.jpg" ]
            }
          ]
        }
        """;

        var result = await Service(HttpStatusCode.OK, body).LookupAsync("889198000000");

        Assert.NotNull(result);
        Assert.Equal("Pokemon Scarlet & Violet Booster Box", result!.Title);
        Assert.Equal("Pokemon", result.Brand);
        Assert.Equal("https://img.example/box.jpg", result.ImageUrl);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_WhenNoItems()
    {
        var result = await Service(HttpStatusCode.OK, """{ "code": "OK", "total": 0, "items": [] }""")
            .LookupAsync("000000000000");

        Assert.Null(result);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_OnHttpError()
    {
        var result = await Service(HttpStatusCode.TooManyRequests, "rate limited").LookupAsync("123");
        Assert.Null(result);
    }

    [Fact]
    public async Task LookupAsync_ReturnsNull_OnMalformedJson()
    {
        var result = await Service(HttpStatusCode.OK, "not json at all").LookupAsync("123");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LookupAsync_ReturnsNull_ForBlankUpc(string upc)
    {
        var result = await Service(HttpStatusCode.OK, "{}").LookupAsync(upc);
        Assert.Null(result);
    }

    [Fact]
    public async Task LookupAsync_SkipsEmptyImageEntries()
    {
        const string body = """
        {
          "items": [ { "title": "Box", "images": [ "", "  ", "https://img/real.png" ] } ]
        }
        """;

        var result = await Service(HttpStatusCode.OK, body).LookupAsync("123");

        Assert.Equal("https://img/real.png", result!.ImageUrl);
    }
}
