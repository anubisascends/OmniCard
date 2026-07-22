using Moq;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.TcgOrderImport;
using Xunit;

namespace OmniCard.Tests.Views.Sales;

public class TcgOrderImportViewModelTests
{
    private static TcgOrderImportPreview Preview() => new()
    {
        Rows =
        {
            new TcgOrderImportRow { OrderNumber = "A", CustomerName = "Ada", Include = true },
            new TcgOrderImportRow { OrderNumber = "B", CustomerName = "Bo", IsDuplicateOrder = true, Include = false },
        },
    };

    [Fact]
    public void LoadPreview_PopulatesRows_AndCanImportWhenAnyIncluded()
    {
        var vm = new TcgOrderImportViewModel(Mock.Of<ITcgPlayerOrderImportService>());
        vm.LoadPreview(Preview());
        Assert.Equal(2, vm.Rows.Count);
        Assert.True(vm.CanImport);
    }

    [Fact]
    public void Import_CommitsPreview_SetsImportedCount_AndCloses()
    {
        var preview = Preview();
        var svc = new Mock<ITcgPlayerOrderImportService>();
        svc.Setup(s => s.Commit(preview)).Returns(1);
        var vm = new TcgOrderImportViewModel(svc.Object);
        vm.LoadPreview(preview);

        bool? closed = null;
        vm.CloseDialog = r => closed = r;
        vm.ImportCommand.Execute(null);

        Assert.Equal(1, vm.ImportedCount);
        Assert.True(closed);
        svc.Verify(s => s.Commit(preview), Times.Once);
    }
}
