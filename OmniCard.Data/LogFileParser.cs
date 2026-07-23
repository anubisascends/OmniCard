using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniCard.Data;

/// <summary>
/// Parses Serilog text log files (written with the app's output template) back into
/// <see cref="LogEntry"/> records. A new entry begins at each header line; lines that do not match
/// the header (exception text, stack traces, wrapped messages) are appended to the current entry.
/// Never throws on malformed content — unparseable leading lines are skipped.
/// </summary>
public sealed partial class LogFileParser
{
    // Matches: 2026-07-23 10:30:45.123 +00:00 [INF] Source.Context: message
    [GeneratedRegex(
        @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<lvl>[A-Z]{3})\] (?<src>.*?): (?<msg>.*)$")]
    private static partial Regex HeaderRegex();

    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz";

    public IReadOnlyList<LogEntry> Parse(string content)
    {
        var entries = new List<LogEntry>();
        if (string.IsNullOrWhiteSpace(content))
            return entries;

        var lines = content.Replace("\r\n", "\n").Split('\n');

        Match? headerMatch = null;
        var raw = new StringBuilder();
        var detail = new StringBuilder();

        void Flush()
        {
            if (headerMatch is null)
                return;

            DateTimeOffset.TryParseExact(
                headerMatch.Groups["ts"].Value, TimestampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts);

            entries.Add(new LogEntry
            {
                Timestamp = ts,
                Level = LogLevelCodes.Parse(headerMatch.Groups["lvl"].Value),
                Source = headerMatch.Groups["src"].Value,
                Message = headerMatch.Groups["msg"].Value,
                Detail = detail.ToString().TrimEnd('\n'),
                Raw = raw.ToString().TrimEnd('\n'),
            });
        }

        foreach (var line in lines)
        {
            var match = HeaderRegex().Match(line);
            if (match.Success)
            {
                Flush();
                headerMatch = match;
                raw.Clear().Append(line).Append('\n');
                detail.Clear();
            }
            else if (headerMatch is not null)
            {
                raw.Append(line).Append('\n');
                detail.Append(line).Append('\n');
            }
            // else: content before the first header — skip.
        }

        Flush();
        return entries;
    }
}
