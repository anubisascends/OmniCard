using System.Linq;
using OmniCard.eBay;

namespace OmniCard.Tests.Services;

public class CredentialStoreTests
{
    /// <summary>
    /// Reproduces the Windows Credential Manager constraint: a single credential
    /// blob cannot exceed 2560 bytes. AdysTech stores the password as UTF-16
    /// (2 bytes/char), so any single write over 1280 chars throws — which is
    /// exactly what broke eBay token persistence.
    /// </summary>
    private sealed class LimitedCredentialStore : CredentialStore
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);
        public const int MaxBytes = 2560;

        protected override string? RawGet(string target) => _store.GetValueOrDefault(target);

        protected override void RawSet(string target, string value)
        {
            if (value.Length * 2 > MaxBytes)
                throw new InvalidOperationException("Credential cannot be more than 2560 bytes long");
            _store[target] = value;
        }

        protected override void RawDelete(string target) => _store.Remove(target);

        protected override bool RawExists(string target) => _store.ContainsKey(target);

        public int RawEntryCount => _store.Count;
    }

    [Fact]
    public void SetGet_RoundTripsSmallValue()
    {
        var store = new LimitedCredentialStore();
        store.Set("k", "hello");
        Assert.Equal("hello", store.Get("k"));
        Assert.Equal(1, store.RawEntryCount);
    }

    [Fact]
    public void SetGet_RoundTripsValueLargerThanCredentialLimit()
    {
        var store = new LimitedCredentialStore();
        var big = new string('x', 4000); // > 2560 bytes as UTF-16
        store.Set("token", big);
        Assert.Equal(big, store.Get("token"));
    }

    [Fact]
    public void SetGet_RoundTripsRealisticEbayToken()
    {
        var store = new LimitedCredentialStore();
        // eBay OAuth access tokens routinely exceed 3000 characters.
        var token = string.Concat(Enumerable.Range(0, 3500).Select(i => (char)('A' + (i % 26))));
        store.Set("OmniCard:eBay:AccessToken", token);
        Assert.Equal(token, store.Get("OmniCard:eBay:AccessToken"));
    }

    [Fact]
    public void Delete_RemovesAllChunks()
    {
        var store = new LimitedCredentialStore();
        store.Set("token", new string('y', 5000));
        Assert.True(store.Exists("token"));

        store.Delete("token");

        Assert.False(store.Exists("token"));
        Assert.Null(store.Get("token"));
        Assert.Equal(0, store.RawEntryCount); // no orphan chunk entries left behind
    }

    [Fact]
    public void Set_OverwritingLargeWithSmall_LeavesNoOrphanChunks()
    {
        var store = new LimitedCredentialStore();
        store.Set("token", new string('z', 5000));
        store.Set("token", "small");

        Assert.Equal("small", store.Get("token"));
        Assert.Equal(1, store.RawEntryCount);
    }

    [Fact]
    public void Get_ReturnsNull_WhenMissing()
    {
        var store = new LimitedCredentialStore();
        Assert.Null(store.Get("nope"));
    }
}
