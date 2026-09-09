using OmniCard.Shared.Settings;

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
