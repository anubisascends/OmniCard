using OmniCard.Models;
using OmniCard.Views.Root;
using Xunit;

namespace OmniCard.Tests.ViewModels;

public class SearchParametersTests
{
    [Fact]
    public void Equal_WhenAllFieldsMatch_AndSamePresetInstances()
    {
        var sort = new SortPreset { Name = "A", Game = CardGame.Mtg };
        var filter = new FilterPreset { Name = "F", Game = CardGame.Mtg };

        var a = new CollectionViewModel.SearchParameters("goblin", CardGame.Mtg, 5, sort, filter, true);
        var b = new CollectionViewModel.SearchParameters("goblin", CardGame.Mtg, 5, sort, filter, true);

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void NotEqual_WhenQueryDiffers()
    {
        var a = new CollectionViewModel.SearchParameters("goblin", CardGame.Mtg, null, null, null, false);
        var b = new CollectionViewModel.SearchParameters("elf", CardGame.Mtg, null, null, null, false);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NotEqual_WhenContainerFilterDiffers()
    {
        var a = new CollectionViewModel.SearchParameters("", CardGame.Mtg, 1, null, null, false);
        var b = new CollectionViewModel.SearchParameters("", CardGame.Mtg, 2, null, null, false);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NotEqual_WhenSortPresetIsDifferentInstance_EvenWithSameName()
    {
        // Ad-hoc sort creates a fresh SortPreset instance each search; it must read as changed.
        var s1 = new SortPreset { Name = "Ad-hoc", Game = CardGame.Mtg };
        var s2 = new SortPreset { Name = "Ad-hoc", Game = CardGame.Mtg };

        var a = new CollectionViewModel.SearchParameters("", CardGame.Mtg, null, s1, null, false);
        var b = new CollectionViewModel.SearchParameters("", CardGame.Mtg, null, s2, null, false);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NotEqual_WhenStackedDiffers()
    {
        var a = new CollectionViewModel.SearchParameters("", CardGame.Mtg, null, null, null, true);
        var b = new CollectionViewModel.SearchParameters("", CardGame.Mtg, null, null, null, false);

        Assert.NotEqual(a, b);
    }
}
