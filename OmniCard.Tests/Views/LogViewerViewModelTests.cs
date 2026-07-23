using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Views.LogViewer;

namespace OmniCard.Tests.Views;

public class LogViewerViewModelTests
{
    private sealed class FakeDataPathService : IDataPathService
    {
        public string DataDirectory => "";
        public string ScansDirectory => "";
        public string TempScansDirectory => "";
        public string SymbolsCacheDirectory => "";
        public string LogsDirectory => "";
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }

    private static LogViewerViewModel CreateVm() =>
        new(new FakeDataPathService(), new LogFileParser());

    private static LogEntry Entry(LogEntryLevel level, string source, string message, int hour) => new()
    {
        Timestamp = new DateTimeOffset(2026, 7, 23, hour, 0, 0, TimeSpan.Zero),
        Level = level,
        Source = source,
        Message = message,
        Raw = $"[{level}] {source}: {message}",
    };

    [Fact]
    public void FilterFor_HidesDeselectedLevels()
    {
        var vm = CreateVm();
        vm.ShowInformation = false;
        var source = new[]
        {
            Entry(LogEntryLevel.Information, "A", "info", 10),
            Entry(LogEntryLevel.Error, "B", "err", 10),
        };

        var result = vm.FilterFor(source);

        Assert.Equal(LogEntryLevel.Error, Assert.Single(result).Level);
    }

    [Fact]
    public void FilterFor_SearchMatchesMessageOrSource_CaseInsensitive()
    {
        var vm = CreateVm();
        vm.SearchText = "scanner";
        var source = new[]
        {
            Entry(LogEntryLevel.Information, "OmniCard.Scanner", "ok", 10),
            Entry(LogEntryLevel.Information, "OmniCard.Web", "SCANNER started", 10),
            Entry(LogEntryLevel.Information, "OmniCard.Web", "unrelated", 10),
        };

        var result = vm.FilterFor(source);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FilterFor_TimeRange_FiltersByHour()
    {
        var vm = CreateVm();
        vm.FromTimeText = "09:00";
        vm.ToTimeText = "11:00";
        var source = new[]
        {
            Entry(LogEntryLevel.Information, "A", "early", 8),
            Entry(LogEntryLevel.Information, "A", "mid", 10),
            Entry(LogEntryLevel.Information, "A", "late", 12),
        };

        var result = vm.FilterFor(source);

        Assert.Equal("mid", Assert.Single(result).Message);
    }

    [Fact]
    public void BuildClipboardText_JoinsRawWithBlankLine()
    {
        var text = LogViewerViewModel.BuildClipboardText(new[]
        {
            Entry(LogEntryLevel.Error, "A", "one", 10),
            Entry(LogEntryLevel.Information, "B", "two", 10),
        });

        Assert.Equal("[Error] A: one\n\n[Information] B: two", text.Replace("\r\n", "\n"));
    }
}
