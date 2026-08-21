using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Models;
using OmniCard.Tests.Fakes;
using OmniCard.Views.ManualAdd;
using Xunit;

namespace OmniCard.Tests.ViewModels;

public class ManualAddViewModelTests
{
    private static (ManualAddViewModel vm, ConfigurableGameService gs, RecordingCardService cards) Build()
    {
        var gs = new ConfigurableGameService
        {
            Sets =
            [
                new SetInfo("MOM", "March of the Machine"),
                new SetInfo("WOE", "Wilds of Eldraine"),
            ],
        };
        var cards = new RecordingCardService(gs);
        var containers = new RecordingContainerService();
        var vm = new ManualAddViewModel(cards, containers, NullLogger<ManualAddViewModel>.Instance);
        vm.Load();
        return (vm, gs, cards);
    }

    [Fact]
    public void Load_PopulatesSetsWithAllSetsSentinelFirst()
    {
        var (vm, _, _) = Build();

        Assert.Equal(3, vm.AvailableSets.Count);
        Assert.Equal("", vm.AvailableSets[0].SetCode);
        Assert.Equal("All Sets", vm.AvailableSets[0].SetName);
        Assert.Same(vm.AvailableSets[0], vm.SelectedSet); // defaults to no filter
    }

    [Fact]
    public void Search_WithSelectedSet_AppendsSetToken()
    {
        var (vm, gs, _) = Build();
        string? seen = null;
        gs.OnSearchCards = (q, _) => { seen = q; return []; };

        vm.SearchQuery = "bolt";
        vm.SelectedSet = vm.AvailableSets[1]; // MOM
        vm.SearchCommand.Execute(null);

        Assert.Equal("bolt set:MOM", seen);
    }

    [Fact]
    public void Search_WithCollectorNumber_AppendsCnToken()
    {
        var (vm, gs, _) = Build();
        string? seen = null;
        gs.OnSearchCards = (q, _) => { seen = q; return []; };

        vm.CollectorNumber = "123";
        vm.SearchCommand.Execute(null);

        Assert.Equal("cn:123", seen);
    }

    [Fact]
    public void Search_CombinesNameSetAndCollectorNumber()
    {
        var (vm, gs, _) = Build();
        string? seen = null;
        gs.OnSearchCards = (q, _) => { seen = q; return []; };

        vm.SearchQuery = "sol ring";
        vm.SelectedSet = vm.AvailableSets[2]; // WOE
        vm.CollectorNumber = "1";
        vm.SearchCommand.Execute(null);

        Assert.Equal("sol ring set:WOE cn:1", seen);
    }

    [Fact]
    public void Search_AllSetsSentinel_AddsNoSetToken()
    {
        var (vm, gs, _) = Build();
        string? seen = null;
        gs.OnSearchCards = (q, _) => { seen = q; return []; };

        vm.SearchQuery = "counterspell";
        vm.SelectedSet = vm.AvailableSets[0]; // All Sets
        vm.SearchCommand.Execute(null);

        Assert.Equal("counterspell", seen);
    }

    [Fact]
    public void Search_WithNoInput_DoesNotQueryAndReportsStatus()
    {
        var (vm, gs, _) = Build();
        var called = false;
        gs.OnSearchCards = (_, _) => { called = true; return []; };

        vm.SearchCommand.Execute(null);

        Assert.False(called);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }
}
