using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class BinderSheetLayoutTests
{
    [Fact]
    public void Parse_WithCsv_RoundTrips()
    {
        var layout = BinderSheetLayout.Parse("2,2,1", totalPages: 0);

        Assert.Equal(new[] { 2, 2, 1 }, layout.Sides);
        Assert.Equal(3, layout.SheetCount);
        Assert.Equal(5, layout.TotalPages);
        Assert.Equal("2,2,1", layout.Serialize());
    }

    [Theory]
    [InlineData(0, new[] { 2 })]   // never zero sheets
    [InlineData(1, new[] { 1 })]
    [InlineData(2, new[] { 2 })]
    [InlineData(5, new[] { 2, 2, 1 })]
    [InlineData(6, new[] { 2, 2, 2 })]
    public void Parse_LegacyBackfill_GroupsIntoSheets(int totalPages, int[] expected)
    {
        var layout = BinderSheetLayout.Parse(sheetSides: null, totalPages);

        Assert.Equal(expected, layout.Sides);
        // Backfill must preserve the original page count so existing card page numbers stay valid.
        if (totalPages >= 1) Assert.Equal(totalPages, layout.TotalPages);
    }

    [Fact]
    public void FirstPageOfSheet_WalksSideCounts()
    {
        var layout = BinderSheetLayout.Parse("2,1,2", totalPages: 0);

        Assert.Equal(1, layout.FirstPageOfSheet(0)); // pages 1,2
        Assert.Equal(3, layout.FirstPageOfSheet(1)); // page 3
        Assert.Equal(4, layout.FirstPageOfSheet(2)); // pages 4,5
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    [InlineData(6, -1)] // past the end
    public void SheetIndexOfPage_MapsPageToOwningSheet(int page, int expectedSheet)
    {
        var layout = BinderSheetLayout.Parse("2,1,2", totalPages: 0);

        Assert.Equal(expectedSheet, layout.SheetIndexOfPage(page));
    }

    [Theory]
    // "2,1,2" → pages [1,2][3][4,5]. Double-sided sheets pair front/back; the single-sided sheet
    // (page 3) has no reverse; out-of-range pages have none either.
    [InlineData(1, 2)]     // front → back
    [InlineData(2, 1)]     // back → front
    [InlineData(3, null)]  // single-sided sheet: nothing behind it
    [InlineData(4, 5)]
    [InlineData(5, 4)]
    [InlineData(6, null)]  // past the end
    [InlineData(0, null)]  // before the first page
    public void ReversePageOf_ReturnsOtherSideOfSameSheet(int page, int? expected)
    {
        var layout = BinderSheetLayout.Parse("2,1,2", totalPages: 0);

        Assert.Equal(expected, layout.ReversePageOf(page));
    }

    [Fact]
    public void Append_AddsSheet_WithoutTouchingExisting()
    {
        var layout = BinderSheetLayout.Parse("2,2", totalPages: 0);

        var withDouble = layout.Append(doubleSided: true);
        var withSingle = layout.Append(doubleSided: false);

        Assert.Equal(new[] { 2, 2, 2 }, withDouble.Sides);
        Assert.Equal(new[] { 2, 2, 1 }, withSingle.Sides);
        Assert.Equal(new[] { 2, 2 }, layout.Sides); // original unchanged (value semantics)
    }

    [Fact]
    public void NewDefault_IsOneDoubleSidedSheet()
    {
        var layout = BinderSheetLayout.NewDefault();

        Assert.Equal(new[] { 2 }, layout.Sides);
        Assert.Equal(2, layout.TotalPages);
    }

    [Fact]
    public void RemoveSheet_FirstSheet_UnplacesItsPages_AndShiftsRestDown()
    {
        var layout = BinderSheetLayout.Parse("2,2,2", totalPages: 0);

        var (result, remap) = layout.RemoveSheet(0);

        Assert.Equal(new[] { 2, 2 }, result.Sides);
        // Sheet 0 (pages 1,2) removed; pages 3-6 shift down by 2.
        Assert.Null(remap[1]);
        Assert.Null(remap[2]);
        Assert.Equal(1, remap[3]);
        Assert.Equal(2, remap[4]);
        Assert.Equal(3, remap[5]);
        Assert.Equal(4, remap[6]);
    }

    [Fact]
    public void RemoveSheet_MiddleSingleSidedSheet_ShiftsTrailingByOne()
    {
        var layout = BinderSheetLayout.Parse("2,1,2", totalPages: 0); // pages: [1,2][3][4,5]

        var (result, remap) = layout.RemoveSheet(1);

        Assert.Equal(new[] { 2, 2 }, result.Sides);
        // Pages 1,2 are before the removed sheet -> unchanged, omitted from the map.
        Assert.False(remap.ContainsKey(1));
        Assert.False(remap.ContainsKey(2));
        Assert.Null(remap[3]);        // the removed single-sided page
        Assert.Equal(3, remap[4]);    // trailing pages shift down by 1
        Assert.Equal(4, remap[5]);
    }

    [Fact]
    public void RemoveSheet_LastSheet_LeavesEarlierPagesUntouched()
    {
        var layout = BinderSheetLayout.Parse("2,2", totalPages: 0);

        var (result, remap) = layout.RemoveSheet(1);

        Assert.Equal(new[] { 2 }, result.Sides);
        Assert.Null(remap[3]);
        Assert.Null(remap[4]);
        Assert.False(remap.ContainsKey(1));
        Assert.False(remap.ContainsKey(2));
    }

    [Fact]
    public void InsertSheet_InMiddle_ShiftsLaterPagesUp()
    {
        var layout = BinderSheetLayout.Parse("2,2", totalPages: 0); // pages [1,2][3,4]

        var (result, remap) = layout.InsertSheet(insertIndex: 1, doubleSided: true);

        Assert.Equal(new[] { 2, 2, 2 }, result.Sides);
        // Pages 1,2 unchanged; the new sheet takes pages 3,4; old 3,4 become 5,6.
        Assert.False(remap.ContainsKey(1));
        Assert.False(remap.ContainsKey(2));
        Assert.Equal(5, remap[3]);
        Assert.Equal(6, remap[4]);
    }

    [Fact]
    public void InsertSheet_SingleSidedAtStart_ShiftsEverythingUpByOne()
    {
        var layout = BinderSheetLayout.Parse("2,2", totalPages: 0);

        var (result, remap) = layout.InsertSheet(insertIndex: 0, doubleSided: false);

        Assert.Equal(new[] { 1, 2, 2 }, result.Sides);
        Assert.Equal(2, remap[1]);
        Assert.Equal(3, remap[2]);
        Assert.Equal(4, remap[3]);
        Assert.Equal(5, remap[4]);
    }

    [Fact]
    public void InsertSheet_AtEnd_IsAppend_NoRemap()
    {
        var layout = BinderSheetLayout.Parse("2,2", totalPages: 0);

        var (result, remap) = layout.InsertSheet(insertIndex: 2, doubleSided: true);

        Assert.Equal(new[] { 2, 2, 2 }, result.Sides);
        Assert.Empty(remap);
    }

    [Fact]
    public void MoveSheet_FirstToEnd_ShiftsOthersDown_AndFirstToBack()
    {
        var layout = BinderSheetLayout.Parse("2,2,2", totalPages: 0); // [1,2][3,4][5,6]

        var (result, remap) = layout.MoveSheet(fromIndex: 0, toIndex: 2); // A -> end

        Assert.Equal(new[] { 2, 2, 2 }, result.Sides);
        // New order B,C,A: B 3,4->1,2 ; C 5,6->3,4 ; A 1,2->5,6
        Assert.Equal(5, remap[1]);
        Assert.Equal(6, remap[2]);
        Assert.Equal(1, remap[3]);
        Assert.Equal(2, remap[4]);
        Assert.Equal(3, remap[5]);
        Assert.Equal(4, remap[6]);
    }

    [Fact]
    public void MoveSheet_LastToFront_PullsItToPageOne()
    {
        var layout = BinderSheetLayout.Parse("2,2,2", totalPages: 0);

        var (result, remap) = layout.MoveSheet(fromIndex: 2, toIndex: 0); // C -> front

        // New order C,A,B: C 5,6->1,2 ; A 1,2->3,4 ; B 3,4->5,6
        Assert.Equal(3, remap[1]);
        Assert.Equal(4, remap[2]);
        Assert.Equal(5, remap[3]);
        Assert.Equal(6, remap[4]);
        Assert.Equal(1, remap[5]);
        Assert.Equal(2, remap[6]);
    }

    [Fact]
    public void MoveSheet_MixedSides_PreservesWithinSheetOffsets()
    {
        var layout = BinderSheetLayout.Parse("2,1,2", totalPages: 0); // [1,2][3][4,5]

        var (result, remap) = layout.MoveSheet(fromIndex: 0, toIndex: 2); // A(2) -> end

        Assert.Equal(new[] { 1, 2, 2 }, result.Sides); // order B(1),C(2),A(2)
        // B 3->1 ; C 4,5->2,3 ; A 1,2->4,5
        Assert.Equal(1, remap[3]);
        Assert.Equal(2, remap[4]);
        Assert.Equal(3, remap[5]);
        Assert.Equal(4, remap[1]);
        Assert.Equal(5, remap[2]);
    }

    [Fact]
    public void MoveSheet_ToSamePosition_IsNoOp()
    {
        var layout = BinderSheetLayout.Parse("2,2,2", totalPages: 0);

        var (result, remap) = layout.MoveSheet(fromIndex: 1, toIndex: 1);

        Assert.Equal(new[] { 2, 2, 2 }, result.Sides);
        Assert.Empty(remap);
    }
}
