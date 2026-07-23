namespace OmniCard.Data;

/// <summary>The six Serilog levels, matching the [{Level:u3}] codes written to the log file.</summary>
public enum LogEntryLevel
{
    Verbose,
    Debug,
    Information,
    Warning,
    Error,
    Fatal,
}

/// <summary>
/// One parsed log entry. <see cref="Raw"/> is the entry's original text (header line plus any
/// continuation lines such as an exception/stack trace), with line endings normalized to \n,
/// and is what the viewer copies to the clipboard.
/// </summary>
public sealed record LogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required LogEntryLevel Level { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }

    /// <summary>Continuation lines (exception text / stack trace) joined by newlines; empty when none.</summary>
    public string Detail { get; init; } = "";

    /// <summary>The entry's original text (header line plus any continuation lines), with line endings normalized to \n. This is what the viewer copies to the clipboard.</summary>
    public required string Raw { get; init; }
}

/// <summary>Maps Serilog's three-letter level codes to <see cref="LogEntryLevel"/>.</summary>
public static class LogLevelCodes
{
    public static LogEntryLevel Parse(string code) => code switch
    {
        "VRB" => LogEntryLevel.Verbose,
        "DBG" => LogEntryLevel.Debug,
        "INF" => LogEntryLevel.Information,
        "WRN" => LogEntryLevel.Warning,
        "ERR" => LogEntryLevel.Error,
        "FTL" => LogEntryLevel.Fatal,
        _ => LogEntryLevel.Information,
    };
}
