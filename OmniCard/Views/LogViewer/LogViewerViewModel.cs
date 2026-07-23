using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Data;
using OmniCard.Interfaces;

namespace OmniCard.Views.LogViewer;

/// <summary>
/// Backs the Log Viewer dialog: enumerates Serilog log files, parses the selected file into
/// <see cref="LogEntry"/> records off the UI thread, and applies level/time/text filters in-memory
/// so toggling is instant. Copies selected entries' raw text to the clipboard for AI examination.
/// </summary>
public sealed partial class LogViewerViewModel(
    IDataPathService dataPathService,
    LogFileParser parser) : ViewModel
{
    private readonly List<LogEntry> _allEntries = [];

    public ObservableCollection<LogFileParser.LogFileInfo> AvailableFiles { get; } = [];
    public ObservableCollection<LogEntry> Entries { get; } = [];

    [ObservableProperty]
    public partial LogFileParser.LogFileInfo? SelectedFile { get; set; }

    async partial void OnSelectedFileChanged(LogFileParser.LogFileInfo? value)
    {
        if (value is not null)
            await ReparseAsync(value.FullPath);
    }

    [ObservableProperty] public partial bool ShowVerbose { get; set; } = true;
    [ObservableProperty] public partial bool ShowDebug { get; set; } = true;
    [ObservableProperty] public partial bool ShowInformation { get; set; } = true;
    [ObservableProperty] public partial bool ShowWarning { get; set; } = true;
    [ObservableProperty] public partial bool ShowError { get; set; } = true;
    [ObservableProperty] public partial bool ShowFatal { get; set; } = true;

    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial string FromTimeText { get; set; } = "";
    [ObservableProperty] public partial string ToTimeText { get; set; } = "";

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? StatusMessage { get; set; }
    [ObservableProperty]
    public partial bool ShowNoMatchOverlay { get; set; }

    partial void OnShowVerboseChanged(bool value) => ApplyFilters();
    partial void OnShowDebugChanged(bool value) => ApplyFilters();
    partial void OnShowInformationChanged(bool value) => ApplyFilters();
    partial void OnShowWarningChanged(bool value) => ApplyFilters();
    partial void OnShowErrorChanged(bool value) => ApplyFilters();
    partial void OnShowFatalChanged(bool value) => ApplyFilters();
    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnFromTimeTextChanged(string value) => ApplyFilters();
    partial void OnToTimeTextChanged(string value) => ApplyFilters();

    /// <summary>Enumerates files and loads the newest one. Call once when the dialog opens.</summary>
    public void Load()
    {
        AvailableFiles.Clear();
        foreach (var f in parser.ListFiles(dataPathService.LogsDirectory))
            AvailableFiles.Add(f);

        if (AvailableFiles.Count == 0)
        {
            StatusMessage = "No log files found.";
            return;
        }

        SelectedFile = AvailableFiles[0]; // triggers OnSelectedFileChanged -> ReparseAsync
    }

    [RelayCommand]
    private async Task Refresh()
    {
        var current = SelectedFile?.FullPath;
        AvailableFiles.Clear();
        foreach (var f in parser.ListFiles(dataPathService.LogsDirectory))
            AvailableFiles.Add(f);

        if (AvailableFiles.Count == 0)
        {
            StatusMessage = "No log files found.";
            _allEntries.Clear();
            ApplyFilters();
            return;
        }

        var target = AvailableFiles.FirstOrDefault(f => f.FullPath == current) ?? AvailableFiles[0];
        SelectedFile = target;            // updates the combo; may or may not raise the change handler
        await ReparseAsync(target.FullPath); // explicit reparse always refreshes (coalesced if one is running)
    }

    private string? _pendingPath;
    private bool _isParsing;

    private async Task ReparseAsync(string path)
    {
        _pendingPath = path;
        if (_isParsing) return;   // an in-flight loop will pick up the latest _pendingPath
        _isParsing = true;
        IsBusy = true;
        try
        {
            while (_pendingPath is { } next)
            {
                _pendingPath = null;
                StatusMessage = null;
                var parsed = await Task.Run(() => parser.ParseFile(next));
                _allEntries.Clear();
                _allEntries.AddRange(parsed);
                if (parsed.Count == 0)
                    StatusMessage = "No log entries in this file (or it could not be read).";
                ApplyFilters();
            }
        }
        finally
        {
            _isParsing = false;
            IsBusy = false;
        }
    }

    private void ApplyFilters()
    {
        Entries.Clear();
        foreach (var e in FilterFor(_allEntries))
            Entries.Add(e);
        ShowNoMatchOverlay = _allEntries.Count > 0 && Entries.Count == 0;
    }

    /// <summary>Applies the current level/time/text filters. Exposed for unit tests.</summary>
    internal IReadOnlyList<LogEntry> FilterFor(IEnumerable<LogEntry> source)
    {
        var from = ParseTime(FromTimeText);
        var to = ParseTime(ToTimeText);
        var search = SearchText?.Trim() ?? "";

        return source.Where(e =>
                IsLevelVisible(e.Level)
                && (from is null || e.Timestamp.TimeOfDay >= from)
                && (to is null || e.Timestamp.TimeOfDay <= to)
                && (search.Length == 0
                    || e.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || e.Source.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private bool IsLevelVisible(LogEntryLevel level) => level switch
    {
        LogEntryLevel.Verbose => ShowVerbose,
        LogEntryLevel.Debug => ShowDebug,
        LogEntryLevel.Information => ShowInformation,
        LogEntryLevel.Warning => ShowWarning,
        LogEntryLevel.Error => ShowError,
        LogEntryLevel.Fatal => ShowFatal,
        _ => true,
    };

    private static TimeSpan? ParseTime(string text) =>
        TimeSpan.TryParseExact(text?.Trim(), ["hh\\:mm", "h\\:mm", "hh\\:mm\\:ss"],
            CultureInfo.InvariantCulture, out var ts) ? ts : null;

    [RelayCommand]
    private void CopySelected(IList? selected)
    {
        var entries = selected?.OfType<LogEntry>().ToList() ?? [];
        if (entries.Count == 0) return;
        try
        {
            Clipboard.SetText(BuildClipboardText(entries));
            StatusMessage = $"Copied {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} to clipboard.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Copy failed: {ex.Message}";
        }
    }

    /// <summary>Joins entries' raw text with a blank-line separator. Exposed for tests.</summary>
    public static string BuildClipboardText(IEnumerable<LogEntry> entries) =>
        string.Join("\n\n", entries.Select(e => e.Raw));
}
