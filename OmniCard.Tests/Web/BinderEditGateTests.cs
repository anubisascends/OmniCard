using Microsoft.Extensions.Configuration;
using OmniCard.Web.Services;
using Xunit;

namespace OmniCard.Tests.Web;

public class BinderEditGateTests
{
    private static IConfiguration Config(string? passphrase)
    {
        var dict = new Dictionary<string, string?>();
        if (passphrase is not null) dict[BinderEditGate.ConfigKey] = passphrase;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void IsEnabled_FalseWhenNoPassphraseConfigured()
    {
        Assert.False(BinderEditGate.IsEnabled(Config(null)));
        Assert.False(BinderEditGate.IsEnabled(Config("")));
        Assert.True(BinderEditGate.IsEnabled(Config("secret")));
    }

    [Fact]
    public void Verify_FailsClosedWhenNoPassphraseConfigured()
    {
        // No passphrase configured → nothing unlocks, even an empty entry.
        Assert.False(BinderEditGate.Verify(Config(null), ""));
        Assert.False(BinderEditGate.Verify(Config(null), "anything"));
    }

    [Fact]
    public void Verify_MatchesExactPassphraseOnly()
    {
        var config = Config("hunter2");
        Assert.True(BinderEditGate.Verify(config, "hunter2"));
        Assert.False(BinderEditGate.Verify(config, "hunter3"));
        Assert.False(BinderEditGate.Verify(config, "HUNTER2"));
        Assert.False(BinderEditGate.Verify(config, ""));
        Assert.False(BinderEditGate.Verify(config, null));
    }
}
