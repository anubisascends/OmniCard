using OmniCard.Models;
using OmniCard.Views.Binder;

namespace OmniCard.Tests.Views.Binder;

public class MoveBinderPageViewModelTests
{
    private static List<BinderSheetInfo> Sheets() =>
    [
        new() { SheetIndex = 0, FirstPage = 1, Sides = 2, TotalSheets = 3, Pages = [1, 2] },
        new() { SheetIndex = 1, FirstPage = 3, Sides = 1, TotalSheets = 3, Pages = [3] },
        new() { SheetIndex = 2, FirstPage = 4, Sides = 2, TotalSheets = 3, Pages = [4, 5] },
    ];

    [Fact]
    public void Positions_ExcludeTheMovingSheet_AndAddEnd()
    {
        var vm = new MoveBinderPageViewModel(Sheets(), movingSheetIndex: 0);

        // Two remaining sheets + "To the end".
        Assert.Equal(3, vm.Positions.Count);
        Assert.Equal("Before page 3", vm.Positions[0].Label);
        Assert.Equal("Before pages 4–5", vm.Positions[1].Label);
        Assert.Equal("To the end", vm.Positions[2].Label);
        Assert.Equal(2, vm.Positions[2].ToIndex);
    }

    [Fact]
    public void MovingLabel_DescribesTheGrabbedSheet()
    {
        var vm = new MoveBinderPageViewModel(Sheets(), movingSheetIndex: 2);

        Assert.Equal("Moving pages 4–5", vm.MovingLabel);
    }

    [Fact]
    public void ToResult_ReturnsSelectedDestinationIndex()
    {
        var vm = new MoveBinderPageViewModel(Sheets(), movingSheetIndex: 0)
        {
            SelectedPosition = null,
        };
        Assert.Null(vm.ToResult());

        vm.SelectedPosition = vm.Positions[0];
        Assert.Equal(0, vm.ToResult());
    }
}
