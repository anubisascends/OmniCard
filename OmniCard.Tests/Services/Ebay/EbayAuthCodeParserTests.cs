using OmniCard.eBay;

namespace OmniCard.Tests.Services.Ebay;

public class EbayAuthCodeParserTests
{
    private const string Code = "v^1.1#i^1#f^0#p^3#r^1#I^3#t^Ul41XzA6MEQ5RkU0Q0Q=";
    private static readonly string Encoded = Uri.EscapeDataString(Code);

    [Fact]
    public void ExtractCode_ReadsCodeFromEbayDefaultAcceptPage()
    {
        var url = $"https://auth2.ebay.com/oauth2/ThirdPartyAuthSucessFailure?isAuthSuccessful=true&code={Encoded}&expires_in=299";
        Assert.Equal(Code, EbayAuthCodeParser.ExtractCode(url));
    }

    [Fact]
    public void ExtractCode_ReadsCodeFromUnreachableCallbackAddress()
    {
        // The accepted URL can point anywhere (an old host, a LAN name) — the code is still in the address.
        var url = $"https://45.30.55.1/api/ebay/callback?code={Encoded}&expires_in=299";
        Assert.Equal(Code, EbayAuthCodeParser.ExtractCode(url));
    }

    [Fact]
    public void ExtractCode_TrimsSurroundingWhitespace()
    {
        Assert.Equal(Code, EbayAuthCodeParser.ExtractCode($"  https://example.com/?code={Encoded}\r\n"));
    }

    [Fact]
    public void ExtractCode_AcceptsQueryStringWithoutAddress()
    {
        Assert.Equal(Code, EbayAuthCodeParser.ExtractCode($"code={Encoded}&expires_in=299"));
    }

    [Fact]
    public void ExtractCode_AcceptsBareCode_EncodedOrNot()
    {
        Assert.Equal(Code, EbayAuthCodeParser.ExtractCode(Code));
        Assert.Equal(Code, EbayAuthCodeParser.ExtractCode(Encoded));
    }

    [Theory]
    [InlineData("https://auth2.ebay.com/oauth2/ThirdPartyAuthSucessFailure?isAuthSuccessful=false")]
    [InlineData("https://auth2.ebay.com/oauth2/ThirdPartyAuthSucessFailure?isAuthSuccessful=false&code=abc")]
    [InlineData("https://example.com/api/ebay/callback?error=access_denied")]
    [InlineData("https://example.com/api/ebay/callback")]
    [InlineData("https://example.com/api/ebay/callback?expires_in=299")]
    [InlineData("https://example.com/api/ebay/callback?code=")]
    [InlineData("not a code")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ExtractCode_ReturnsNull_WhenThereIsNoUsableCode(string? input)
    {
        Assert.Null(EbayAuthCodeParser.ExtractCode(input));
    }
}
