using OmniCard.Data;

namespace OmniCard.Tests.Services;

public class LogFileParserTests
{
    private static readonly LogFileParser Parser = new();

    [Fact]
    public void Parse_SingleLineEntry_ExtractsFields()
    {
        const string content =
            "2026-07-23 10:30:45.123 +00:00 [INF] OmniCard.Scanner.ScannerService: Scan committed";

        var entry = Assert.Single(Parser.Parse(content));

        Assert.Equal(LogEntryLevel.Information, entry.Level);
        Assert.Equal("OmniCard.Scanner.ScannerService", entry.Source);
        Assert.Equal("Scan committed", entry.Message);
        Assert.Equal("", entry.Detail);
        Assert.Equal(new DateTimeOffset(2026, 7, 23, 10, 30, 45, 123, TimeSpan.Zero), entry.Timestamp);
        Assert.Equal(content, entry.Raw);
    }

    [Fact]
    public void Parse_MultiLineException_AttachesToParentEntry()
    {
        const string content =
            "2026-07-23 10:30:45.123 +00:00 [ERR] OmniCard.Scanner.ScannerService: Scan failed\n" +
            "System.InvalidOperationException: device not connected\n" +
            "   at OmniCard.Scanner.ScannerService.Scan()\n" +
            "2026-07-23 10:30:46.000 +00:00 [INF] OmniCard.App: Recovered";

        var entries = Parser.Parse(content);

        Assert.Equal(2, entries.Count);
        Assert.Equal(LogEntryLevel.Error, entries[0].Level);
        Assert.Equal("Scan failed", entries[0].Message);
        Assert.Contains("InvalidOperationException", entries[0].Detail);
        Assert.Contains("at OmniCard.Scanner.ScannerService.Scan()", entries[0].Detail);
        Assert.Contains("device not connected", entries[0].Raw);
        Assert.Equal("Recovered", entries[1].Message);
        Assert.Equal("", entries[1].Detail);
    }

    [Fact]
    public void Parse_CrlfInput_NormalizesLineEndingsToLf()
    {
        var content =
            "2026-07-23 10:30:45.123 +00:00 [ERR] Src: boom\r\n" +
            "System.Exception: nope\r\n" +
            "   at Foo.Bar()\r\n";      // trailing CRLF, like a real file
        var entry = Assert.Single(Parser.Parse(content));
        Assert.DoesNotContain('\r', entry.Raw);
        Assert.Equal(
            "2026-07-23 10:30:45.123 +00:00 [ERR] Src: boom\n" +
            "System.Exception: nope\n" +
            "   at Foo.Bar()",
            entry.Raw);
        Assert.Equal("boom", entry.Message);
        Assert.Contains("System.Exception: nope", entry.Detail);
    }

    [Theory]
    [InlineData("VRB", LogEntryLevel.Verbose)]
    [InlineData("DBG", LogEntryLevel.Debug)]
    [InlineData("INF", LogEntryLevel.Information)]
    [InlineData("WRN", LogEntryLevel.Warning)]
    [InlineData("ERR", LogEntryLevel.Error)]
    [InlineData("FTL", LogEntryLevel.Fatal)]
    [InlineData("ZZZ", LogEntryLevel.Information)]
    public void Parse_MapsLevelCodes(string code, LogEntryLevel expected)
    {
        var content = $"2026-07-23 10:30:45.123 +00:00 [{code}] Src: msg";
        Assert.Equal(expected, Assert.Single(Parser.Parse(content)).Level);
    }

    [Fact]
    public void Parse_LeadingJunkBeforeFirstHeader_IsSkipped()
    {
        const string content =
            "garbage line with no header\n" +
            "another one\n" +
            "2026-07-23 10:30:45.123 +00:00 [INF] Src: real";

        var entry = Assert.Single(Parser.Parse(content));
        Assert.Equal("real", entry.Message);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmpty()
    {
        Assert.Empty(Parser.Parse(""));
        Assert.Empty(Parser.Parse("   \n  \n"));
    }

    [Fact]
    public void ListFiles_MissingDirectory_ReturnsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-omnicard-logs-" + Guid.NewGuid());
        Assert.Empty(Parser.ListFiles(missing));
    }

    [Fact]
    public void ListFiles_ReturnsOnlyMatchingFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-logs-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "tcgcardscanner-20260722.log"), "x");
            File.WriteAllText(Path.Combine(dir, "tcgcardscanner-20260723.log"), "x");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");

            var files = Parser.ListFiles(dir);

            Assert.Equal(2, files.Count);
            Assert.All(files, f => Assert.StartsWith("tcgcardscanner-", f.DisplayName));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ListFiles_ReturnsNewestFirst()
    {
        var dir = Path.Combine(Path.GetTempPath(), "omnicard-logs-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var older = Path.Combine(dir, "tcgcardscanner-20260701.log");
            var newer = Path.Combine(dir, "tcgcardscanner-20260702.log");
            File.WriteAllText(older, "x");
            File.WriteAllText(newer, "x");
            File.SetLastWriteTimeUtc(older, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newer, new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));

            var files = new LogFileParser().ListFiles(dir);

            Assert.Equal("tcgcardscanner-20260702.log", files[0].DisplayName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ParseFile_ReadsAndParses()
    {
        var path = Path.Combine(Path.GetTempPath(), "tcgcardscanner-" + Guid.NewGuid() + ".log");
        File.WriteAllText(path, "2026-07-23 10:30:45.123 +00:00 [WRN] Src: heads up");
        try
        {
            var entry = Assert.Single(Parser.ParseFile(path));
            Assert.Equal(LogEntryLevel.Warning, entry.Level);
            Assert.Equal("heads up", entry.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseFile_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(Parser.ParseFile(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".log")));
    }
}
