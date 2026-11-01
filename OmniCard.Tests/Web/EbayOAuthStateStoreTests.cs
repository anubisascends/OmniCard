using System.Buffers.Text;
using System.Text.Json;
using OmniCard.Tests.Web.ScanBatches;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

public class EbayOAuthStateStoreTests
{
    private const string Callback = "http://gizmo:8081/api/ebay/callback";
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_EncodesReturnUrlAsTheBrokerReadsIt()
    {
        var state = new EbayOAuthStateStore(_clock).Create(Callback);

        // The Worker decodes base64url JSON and reads the lowercase "r" property.
        var json = JsonDocument.Parse(Base64Url.DecodeFromChars(state)).RootElement;
        Assert.Equal(Callback, json.GetProperty("r").GetString());
        Assert.False(string.IsNullOrEmpty(json.GetProperty("n").GetString()));
        Assert.DoesNotContain('+', state);
        Assert.DoesNotContain('/', state);
        Assert.DoesNotContain('=', state);
    }

    [Fact]
    public void Consume_AcceptsIssuedStateOnlyOnce()
    {
        var store = new EbayOAuthStateStore(_clock);
        var state = store.Create(Callback);

        Assert.True(store.Consume(state));
        Assert.False(store.Consume(state));
    }

    [Fact]
    public void Consume_RejectsExpiredState()
    {
        var store = new EbayOAuthStateStore(_clock);
        var state = store.Create(Callback);

        _clock.Advance(EbayOAuthStateStore.Lifetime + TimeSpan.FromSeconds(1));

        Assert.False(store.Consume(state));
    }

    [Fact]
    public void Consume_RejectsStateFromAnotherServer()
    {
        var other = new EbayOAuthStateStore(_clock).Create(Callback);
        Assert.False(new EbayOAuthStateStore(_clock).Consume(other));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64-json!")]
    [InlineData("e30")] // "{}"
    public void Consume_RejectsMalformedState(string? state)
    {
        Assert.False(new EbayOAuthStateStore(_clock).Consume(state));
    }
}
