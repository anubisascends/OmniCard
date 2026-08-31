using NTwain.Data;
using OmniCard.Scanner;
using Xunit;

namespace OmniCard.Tests.Services;

/// <summary>
/// Guards the set of OmniCard-managed ("critical") capabilities that must never be overridden by a
/// user profile — changing these would break color matching or image transfer. The applier and the
/// probe both key off this set to skip/lock them.
/// </summary>
public class CapabilityProfileApplierTests
{
    [Theory]
    [InlineData(CapabilityId.ICapPixelType)]   // color mode — matching assumes RGB
    [InlineData(CapabilityId.ICapXferMech)]    // transfer mechanism — must stay memory/native
    [InlineData(CapabilityId.CapXferCount)]    // pinned so ADF batches aren't cut short
    [InlineData(CapabilityId.CapAutoScan)]     // ADF auto-feed
    [InlineData(CapabilityId.CapDuplexEnabled)]
    public void CriticalCaps_Contains_ManagedCapability(CapabilityId cap)
    {
        Assert.Contains(cap, CapabilityProfileApplier.CriticalCaps);
    }

    [Fact]
    public void Apply_NullSettings_DoesNotThrow()
    {
        // No DataSource is dereferenced when settings are null.
        var ex = Record.Exception(() => CapabilityProfileApplier.Apply(null!, null));
        Assert.Null(ex);
    }
}
