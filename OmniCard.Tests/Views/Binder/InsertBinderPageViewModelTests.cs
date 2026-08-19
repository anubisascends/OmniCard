using OmniCard.Models;
using OmniCard.Views.Binder;

namespace OmniCard.Tests.Views.Binder;

public class InsertBinderPageViewModelTests
{
    private static List<BinderSheetInfo> Sheets() =>
    [
        new() { SheetIndex = 0, FirstPage = 1, Sides = 2, TotalSheets = 3, Pages = [1, 2] },
        new() { SheetIndex = 1, FirstPage = 3, Sides = 1, TotalSheets = 3, Pages = [3] },
        new() { SheetIndex = 2, FirstPage = 4, Sides = 2, TotalSheets = 3, Pages = [4, 5] },
    ];

    [Fact]
    public void Positions_IncludeEachSheetPlusEnd()
    {
        var vm = new InsertBinderPageViewModel(Sheets(), nearPage: null);

        Assert.Equal(4, vm.Positions.Count); // 3 sheets + "At the end"
        Assert.Equal("Before pages 1–2", vm.Positions[0].Label);
        Assert.Equal("Before page 3", vm.Positions[1].Label);   // single-sided sheet
        Assert.Equal("Before pages 4–5", vm.Positions[2].Label);
        Assert.Equal("At the end", vm.Positions[3].Label);
        Assert.Equal(3, vm.Positions[3].InsertIndex);
    }

    [Fact]
    public void NearPage_DefaultsSelectionToThatSheet()
    {
        var vm = new InsertBinderPageViewModel(Sheets(), nearPage: 4);

        Assert.Equal(2, vm.SelectedPosition!.InsertIndex); // sheet owning page 4
    }

    [Fact]
    public void NoNearPage_DefaultsToEnd()
    {
        var vm = new InsertBinderPageViewModel(Sheets(), nearPage: null);

        Assert.Equal(3, vm.SelectedPosition!.InsertIndex); // the "At the end" option
    }

    [Fact]
    public void ToResult_ReflectsSelectionAndSideChoice()
    {
        var vm = new InsertBinderPageViewModel(Sheets(), nearPage: 1)
        {
            DoubleSided = false,
        };

        var result = vm.ToResult();

        Assert.NotNull(result);
        Assert.Equal(0, result!.Value.InsertIndex);
        Assert.False(result.Value.DoubleSided);
    }
}
