using System.Collections.Generic;
using System.Linq;
using Moq;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Settings;
using Xunit;

namespace OmniCard.Tests.Views.Settings;

public class SalesWorkflowSettingsViewModelTests
{
    private static SalesWorkflowSettingsViewModel MakeVm(out Mock<ISalesSettingsService> settings)
    {
        settings = new Mock<ISalesSettingsService>();
        settings.Setup(s => s.GetWorkflowLanes()).Returns(WorkflowLane.Defaults());
        return new SalesWorkflowSettingsViewModel(settings.Object);
    }

    [Fact]
    public void Load_PopulatesLanesFromSettings()
    {
        var vm = MakeVm(out _);
        vm.Load();
        Assert.Equal(WorkflowLane.Defaults().Count, vm.Lanes.Count);
        Assert.Equal("Created", vm.Lanes[0].Name);
    }

    [Fact]
    public void AddLane_AppendsUniqueLane_AndSelectsIt()
    {
        var vm = MakeVm(out _);
        vm.Load();
        var before = vm.Lanes.Count;

        vm.AddLaneCommand.Execute(null);

        Assert.Equal(before + 1, vm.Lanes.Count);
        Assert.Same(vm.Lanes[^1], vm.SelectedLane);
        Assert.Equal(vm.Lanes.Count, vm.Lanes.Select(l => l.Key).Distinct().Count()); // keys unique
    }

    [Fact]
    public void MoveLaneUpDown_ReordersLanes()
    {
        var vm = MakeVm(out _);
        vm.Load();
        var second = vm.Lanes[1];

        vm.MoveLaneUpCommand.Execute(second);
        Assert.Same(second, vm.Lanes[0]);

        vm.MoveLaneDownCommand.Execute(second);
        Assert.Same(second, vm.Lanes[1]);
    }

    [Fact]
    public void MoveLane_ToIndex_Reorders()
    {
        var vm = MakeVm(out _);
        vm.Load();
        var first = vm.Lanes[0];

        vm.MoveLane(first, 2);

        Assert.Same(first, vm.Lanes[2]);
    }

    [Fact]
    public void Save_PersistsLanes_InCurrentOrder()
    {
        var vm = MakeVm(out var settings);
        List<WorkflowLane>? saved = null;
        settings.Setup(s => s.SaveWorkflowLanes(It.IsAny<IEnumerable<WorkflowLane>>()))
                .Callback<IEnumerable<WorkflowLane>>(l => saved = l.ToList());
        vm.Load();
        vm.MoveLaneUpCommand.Execute(vm.Lanes[1]); // swap first two

        vm.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal(vm.Lanes.Select(l => l.Key), saved!.Select(l => l.Key));
        Assert.Equal("Saved. Reopen the Orders tab to see the new board.", vm.StatusMessage);
    }

    [Fact]
    public void Save_Rejects_BlankLaneName()
    {
        var vm = MakeVm(out var settings);
        vm.Load();
        vm.Lanes[0].Name = "  ";

        vm.SaveCommand.Execute(null);

        settings.Verify(s => s.SaveWorkflowLanes(It.IsAny<IEnumerable<WorkflowLane>>()), Times.Never);
        Assert.Equal("Every lane needs a name.", vm.StatusMessage);
    }

    [Fact]
    public void RestoreDefaults_ResetsToBuiltInLanes()
    {
        var vm = MakeVm(out _);
        vm.Load();
        vm.Lanes.Clear();

        vm.RestoreDefaultsCommand.Execute(null);

        Assert.Equal(WorkflowLane.Defaults().Count, vm.Lanes.Count);
        Assert.Equal("created", vm.Lanes[0].Key);
    }
}
