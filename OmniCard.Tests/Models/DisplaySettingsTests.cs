using OmniCard.Models;
using Xunit;

namespace OmniCard.Tests.Models;

public class DisplaySettingsTests
{
    [Fact]
    public void SidebarExpanded_DefaultsToTrue()
    {
        var settings = new DisplaySettings();
        Assert.True(settings.SidebarExpanded);
    }
}
