using OmniCard.Controls;
using Xunit;

namespace OmniCard.Tests.Controls;

public class TagTriStateTests
{
    [Fact]
    public void Compute_ZeroOfTotal_ReturnsUnchecked()
        => Assert.Equal(TagCheckState.Unchecked, TagTriState.Compute(countWithTag: 0, totalCount: 3));

    [Fact]
    public void Compute_AllOfTotal_ReturnsChecked()
        => Assert.Equal(TagCheckState.Checked, TagTriState.Compute(countWithTag: 3, totalCount: 3));

    [Fact]
    public void Compute_SomeOfTotal_ReturnsIndeterminate()
        => Assert.Equal(TagCheckState.Indeterminate, TagTriState.Compute(countWithTag: 1, totalCount: 3));

    [Fact]
    public void Compute_SingleItemWithTag_ReturnsChecked()
        => Assert.Equal(TagCheckState.Checked, TagTriState.Compute(countWithTag: 1, totalCount: 1));
}
