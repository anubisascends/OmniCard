using OmniCard.Controls;
using Xunit;

namespace OmniCard.Tests.Controls;

public class TagFlyoutItemTests
{
    [Theory]
    [InlineData(TagCheckState.Checked, true, false)]
    [InlineData(TagCheckState.Unchecked, false, false)]
    [InlineData(TagCheckState.Indeterminate, false, true)]
    public void IsCheckedAndIsIndeterminate_ReflectState(TagCheckState state, bool expectedChecked, bool expectedIndeterminate)
    {
        var item = new TagFlyoutItem("Foil", state);

        Assert.Equal(expectedChecked, item.IsChecked);
        Assert.Equal(expectedIndeterminate, item.IsIndeterminate);
    }

    [Fact]
    public void SettingState_RaisesPropertyChangedForDerivedProperties()
    {
        var item = new TagFlyoutItem("Foil", TagCheckState.Unchecked);
        var raised = new List<string>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        item.State = TagCheckState.Checked;

        Assert.Contains(nameof(TagFlyoutItem.State), raised);
        Assert.Contains(nameof(TagFlyoutItem.IsChecked), raised);
        Assert.Contains(nameof(TagFlyoutItem.IsIndeterminate), raised);
    }

    [Fact]
    public void SettingState_ToSameValue_DoesNotRaisePropertyChanged()
    {
        var item = new TagFlyoutItem("Foil", TagCheckState.Checked);
        var raised = false;
        item.PropertyChanged += (_, _) => raised = true;

        item.State = TagCheckState.Checked;

        Assert.False(raised);
    }
}
