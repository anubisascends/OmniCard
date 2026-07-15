# Scan Matching Integration Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a data sync tool + bat file that extracts test data from the user's scryfall.db and art folder, and an integration test that verifies the pHash matching pipeline correctly identifies scanned card images.

**Architecture:** A standalone console tool parses scan image filenames, queries scryfall.db for matching cards, creates a subset test DB + art files. Integration tests auto-discover all scan images via `[MemberData]`, compute pHash, call `ScryfallService.FindClosestMatch`, and assert correct identification.

**Tech Stack:** C# / .NET 10, Microsoft.Data.Sqlite, xUnit, PerceptualHashService (real), ScryfallService (real with test DB)

## Global Constraints

- Sync tool targets `net10.0` (no Windows TFM — pure SQLite + file I/O)
- Sync tool is standalone — NOT referenced by test project, NOT in the solution
- Test project already has `<Content Include="TestData\**" CopyToOutputDirectory="PreserveNewest" />`
- Tests use `[Theory]` + `[MemberData]` for auto-discovery
- Filename regex: `(?<name>.+) \[(?<set>[A-Za-z0-9]+)\] #(?<num>[^\.\s]+)\.png`
- `ScryfallService` constructor requires: `IHttpClientFactory`, `IDbContextFactory<ScryfallDbContext>`, `IPerceptualHashService`, `SetSymbolCache`, `IOptions<ScryfallSettings>`, `ILogger<ScryfallService>`, `IDataPathService`
- Test DB schema must match what `ScryfallDbContext.OnModelCreating` expects (EF `EnsureCreated` + runtime-migrated columns)

---

### Task 1: Sync Tool + Bat File

**Files:**
- Create: `OmniCard.Tests/Tools/SyncTestData/SyncTestData.csproj`
- Create: `OmniCard.Tests/Tools/SyncTestData/Program.cs`
- Create: `OmniCard.Tests/TestData/sync-test-data.bat`

**Interfaces:**
- Consumes: external `scryfall.db`, external `art/` folder, external scan source folder
- Produces: `OmniCard.Tests/TestData/test-scryfall.db`, `OmniCard.Tests/TestData/art/**/*.jpg`, `OmniCard.Tests/TestData/scans/**/*.png` — used by Task 2 integration test

- [ ] **Step 1: Create the tool project**

Create `OmniCard.Tests/Tools/SyncTestData/SyncTestData.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.9" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Implement Program.cs**

Create `OmniCard.Tests/Tools/SyncTestData/Program.cs`:

```csharp
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

// --- Parse arguments ---
var scryfallDb = @"X:\TCG Card Scanner\scryfall.db";
var artDir = @"X:\TCG Card Scanner\art";
var scanSource = @"C:\Users\anubi\OneDrive\Desktop\Test_Data";

for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--scryfall-db": scryfallDb = args[++i]; break;
        case "--art-dir": artDir = args[++i]; break;
        case "--scan-source": scanSource = args[++i]; break;
    }
}

if (!File.Exists(scryfallDb))
{
    Console.Error.WriteLine($"Error: scryfall.db not found at {scryfallDb}");
    return 1;
}
if (!Directory.Exists(scanSource))
{
    Console.Error.WriteLine($"Error: scan source not found at {scanSource}");
    return 1;
}

// --- Parse filenames to get (SetCode, CollectorNumber) pairs ---
var filenameRegex = new Regex(@"(?<name>.+) \[(?<set>[A-Za-z0-9]+)\] #(?<num>[^\.\s]+)\.png",
    RegexOptions.IgnoreCase);

var scanFiles = Directory.GetFiles(scanSource, "*.png", SearchOption.AllDirectories);
var cardKeys = new HashSet<(string Set, string Num)>(StringComparer.Create(
    System.Globalization.CultureInfo.InvariantCulture, ignoreCase: true));

foreach (var file in scanFiles)
{
    var match = filenameRegex.Match(Path.GetFileName(file));
    if (match.Success)
        cardKeys.Add((match.Groups["set"].Value.ToLowerInvariant(), match.Groups["num"].Value));
}

Console.WriteLine($"Found {scanFiles.Length} scan images, {cardKeys.Count} unique cards to extract");

if (cardKeys.Count == 0)
{
    Console.Error.WriteLine("No card files found matching expected filename pattern.");
    return 1;
}

// --- Resolve output paths ---
// The .csproj lives at OmniCard.Tests/Tools/SyncTestData/SyncTestData.csproj
// TestData is at OmniCard.Tests/TestData/ — two directories up from the .csproj
var projectDir = Path.GetDirectoryName(Environment.ProcessPath) is { } binDir
    ? Path.GetFullPath(Path.Combine(binDir, "..", "..", ".."))  // from bin/Debug/net10.0 up to project dir
    : Environment.CurrentDirectory;
var testDataDir = Path.GetFullPath(Path.Combine(projectDir, "..", "..", "TestData"));
if (!Directory.Exists(testDataDir))
    Directory.CreateDirectory(testDataDir);

var targetDb = Path.Combine(testDataDir, "test-scryfall.db");
var targetArtDir = Path.Combine(testDataDir, "art");
var targetScansDir = Path.Combine(testDataDir, "scans");

// --- Create target DB with subset of cards ---
if (File.Exists(targetDb)) File.Delete(targetDb);

using var sourceConn = new SqliteConnection($"Data Source={scryfallDb};Mode=ReadOnly");
sourceConn.Open();

using var targetConn = new SqliteConnection($"Data Source={targetDb}");
targetConn.Open();

// Copy the schema from source DB
using (var schemaCmd = sourceConn.CreateCommand())
{
    schemaCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND sql IS NOT NULL ORDER BY name";
    using var reader = schemaCmd.ExecuteReader();
    while (reader.Read())
    {
        var createSql = reader.GetString(0);
        using var createCmd = targetConn.CreateCommand();
        createCmd.CommandText = createSql;
        try { createCmd.ExecuteNonQuery(); }
        catch (SqliteException) { /* table may already exist from a dependency */ }
    }
}

// Also copy indexes
using (var idxCmd = sourceConn.CreateCommand())
{
    idxCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND sql IS NOT NULL";
    using var reader = idxCmd.ExecuteReader();
    while (reader.Read())
    {
        using var createCmd = targetConn.CreateCommand();
        createCmd.CommandText = reader.GetString(0);
        try { createCmd.ExecuteNonQuery(); }
        catch (SqliteException) { /* ignore duplicate index */ }
    }
}

// Query matching cards and copy rows
var copiedCards = 0;
var artFilesCopied = 0;
var artPaths = new List<string>();

foreach (var (set, num) in cardKeys)
{
    using var queryCmd = sourceConn.CreateCommand();
    queryCmd.CommandText = "SELECT * FROM Cards WHERE SetCode = @set AND CollectorNumber = @num";
    queryCmd.Parameters.AddWithValue("@set", set);
    queryCmd.Parameters.AddWithValue("@num", num);

    using var reader = queryCmd.ExecuteReader();

    while (reader.Read())
    {
        // Build INSERT dynamically from column names
        var columns = new List<string>();
        var paramNames = new List<string>();
        var values = new List<(string ParamName, object Value)>();

        for (int c = 0; c < reader.FieldCount; c++)
        {
            var colName = reader.GetName(c);
            columns.Add($"\"{colName}\"");
            paramNames.Add($"@p{c}");
            values.Add(($"@p{c}", reader.IsDBNull(c) ? DBNull.Value : reader.GetValue(c)));
        }

        using var insertCmd = targetConn.CreateCommand();
        insertCmd.CommandText = $"INSERT OR IGNORE INTO Cards ({string.Join(",", columns)}) VALUES ({string.Join(",", paramNames)})";
        foreach (var (name, val) in values)
            insertCmd.Parameters.AddWithValue(name, val);
        insertCmd.ExecuteNonQuery();
        copiedCards++;

        // Track art path for copying
        var pathOrdinal = reader.GetOrdinal("LocalImagePath");
        if (!reader.IsDBNull(pathOrdinal))
            artPaths.Add(reader.GetString(pathOrdinal));
    }
}

sourceConn.Close();
targetConn.Close();

Console.WriteLine($"Copied {copiedCards} card rows to test-scryfall.db");

// --- Copy art files ---
foreach (var artRelPath in artPaths.Distinct())
{
    var sourcePath = Path.Combine(artDir, artRelPath.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(sourcePath))
    {
        Console.WriteLine($"  Warning: art file not found: {sourcePath}");
        continue;
    }

    // Map to TestData/art/{set}/{number}.jpg preserving structure
    var targetPath = Path.Combine(targetArtDir, artRelPath.Replace('/', Path.DirectorySeparatorChar));
    var targetDir2 = Path.GetDirectoryName(targetPath);
    if (targetDir2 != null) Directory.CreateDirectory(targetDir2);
    File.Copy(sourcePath, targetPath, overwrite: true);
    artFilesCopied++;
}

Console.WriteLine($"Copied {artFilesCopied} art files");

// --- Copy scan images ---
if (Directory.Exists(targetScansDir))
    Directory.Delete(targetScansDir, true);

foreach (var subDir in Directory.GetDirectories(scanSource))
{
    var dirName = Path.GetFileName(subDir);
    var targetSubDir = Path.Combine(targetScansDir, dirName);
    Directory.CreateDirectory(targetSubDir);

    foreach (var file in Directory.GetFiles(subDir, "*.png"))
    {
        File.Copy(file, Path.Combine(targetSubDir, Path.GetFileName(file)), overwrite: true);
    }
}

var totalScans = Directory.GetFiles(targetScansDir, "*.png", SearchOption.AllDirectories).Length;
Console.WriteLine($"Copied {totalScans} scan images");
Console.WriteLine($"\nSync complete: {copiedCards} cards, {artFilesCopied} art files, {totalScans} scans");
Console.WriteLine($"Output: {testDataDir}");

return 0;
```

- [ ] **Step 3: Create the bat file**

Create `OmniCard.Tests/TestData/sync-test-data.bat`:

```bat
@echo off
REM Sync test data from external sources into TestData/
REM Usage: sync-test-data.bat [--scryfall-db path] [--art-dir path] [--scan-source path]
REM Defaults: scryfall-db = X:\TCG Card Scanner\scryfall.db
REM           art-dir     = X:\TCG Card Scanner\art
REM           scan-source = C:\Users\anubi\OneDrive\Desktop\Test_Data
dotnet run --project "%~dp0..\Tools\SyncTestData\SyncTestData.csproj" -- %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Sync failed with error code %ERRORLEVEL%
    pause
) else (
    echo.
    echo Done! You can now run the integration tests.
)
```

- [ ] **Step 4: Run the sync tool to generate test data**

```bash
cd d:/source/repos/OmniCard/OmniCard.Tests/TestData
cmd.exe /c sync-test-data.bat
```

Expected: `Sync complete: 9 cards, N art files, 18 scans`

- [ ] **Step 5: Verify test data was created**

```bash
ls OmniCard.Tests/TestData/test-scryfall.db
ls OmniCard.Tests/TestData/art/
ls OmniCard.Tests/TestData/scans/
```

Expected: DB file exists, art folders with jpg files, scans with two DPI subfolders.

- [ ] **Step 6: Commit everything**

```bash
git add OmniCard.Tests/Tools/ OmniCard.Tests/TestData/
git commit -m "feat: add sync tool and bat file for scan matching test data"
```

---

### Task 2: Integration Test

**Files:**
- Create: `OmniCard.Tests/Services/ScanMatchingIntegrationTests.cs`
- Modify: `OmniCard.Tests/OmniCard.Tests.csproj` (add TestData content glob if not already present)

**Interfaces:**
- Consumes: `TestData/test-scryfall.db`, `TestData/scans/**/*.png` from Task 1; `PerceptualHashService`, `ScryfallService`, `ScryfallDbContext` from production code
- Produces: Parameterized integration tests that auto-discover and verify all scan images

- [ ] **Step 1: Ensure TestData content glob in csproj**

Check if `OmniCard.Tests/OmniCard.Tests.csproj` already has a `TestData` content glob. If not, add:

```xml
<ItemGroup>
  <Content Include="TestData\**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 2: Create the integration test file**

Create `OmniCard.Tests/Services/ScanMatchingIntegrationTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;
using Xunit.Abstractions;

namespace OmniCard.Tests.Services;

public class ScanMatchingIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempDbPath;
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<ScryfallDbContext> _dbFactory;
    private readonly PerceptualHashService _hashService;
    private readonly ScryfallService _scryfallService;

    private static readonly Regex FilenameRegex = new(
        @"(?<name>.+) \[(?<set>[A-Za-z0-9]+)\] #(?<num>[^\.\s]+)\.png",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ScanMatchingIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        // Copy test DB to temp file to avoid locking the committed file
        var sourceDb = Path.Combine(AppContext.BaseDirectory, "TestData", "test-scryfall.db");
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"test-scryfall-{Guid.NewGuid()}.db");
        File.Copy(sourceDb, _tempDbPath);

        // Open a persistent connection (keeps in-memory-like behavior for the temp file)
        _connection = new SqliteConnection($"Data Source={_tempDbPath}");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ScryfallDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbFactory = new TestScryfallDbFactory(options);

        _hashService = new PerceptualHashService(NullLogger<PerceptualHashService>.Instance);

        // ScryfallService needs several dependencies — stub the ones we don't use
        var dataPathService = new TestDataPathService(
            Path.Combine(AppContext.BaseDirectory, "TestData"));

        _scryfallService = new ScryfallService(
            new StubHttpClientFactory(),
            _dbFactory,
            _hashService,
            new SetSymbolCache(
                new StubHttpClientFactory(),
                dataPathService,
                NullLogger<SetSymbolCache>.Instance),
            Options.Create(new ScryfallSettings { Languages = ["en"] }),
            NullLogger<ScryfallService>.Instance,
            dataPathService);
    }

    public void Dispose()
    {
        _scryfallService.Dispose();
        _connection.Dispose();
        try { File.Delete(_tempDbPath); } catch { /* best effort */ }
    }

    /// <summary>
    /// Auto-discovers all scan images in TestData/scans/ and yields test cases.
    /// Each PNG filename is parsed for expected set code and collector number.
    /// </summary>
    public static IEnumerable<object[]> ScanImageFiles()
    {
        var scansDir = Path.Combine(AppContext.BaseDirectory, "TestData", "scans");
        if (!Directory.Exists(scansDir))
            yield break;

        foreach (var file in Directory.GetFiles(scansDir, "*.png", SearchOption.AllDirectories))
        {
            var match = FilenameRegex.Match(Path.GetFileName(file));
            if (!match.Success) continue;

            var setCode = match.Groups["set"].Value.ToLowerInvariant();
            var collectorNumber = match.Groups["num"].Value;
            var relativePath = Path.GetRelativePath(AppContext.BaseDirectory, file);

            yield return [relativePath, setCode, collectorNumber];
        }
    }

    [Theory]
    [MemberData(nameof(ScanImageFiles))]
    public void ScanImage_MatchesExpectedCard(string relativePath, string expectedSet, string expectedNumber)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        Assert.True(File.Exists(fullPath), $"Scan image not found: {fullPath}");

        // Compute pHash from the scan image
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = _hashService.ComputeHash(stream);

        _output.WriteLine($"File: {Path.GetFileName(fullPath)}");
        _output.WriteLine($"pHash: {hash:X16}");

        // Find the closest match in the test DB
        var match = _scryfallService.FindClosestMatch(hash);

        Assert.NotNull(match);
        _output.WriteLine($"Matched: {match.Name} [{match.SetCode}] #{match.CollectorNumber}");
        _output.WriteLine($"Confidence: {match.Confidence:F1}%");

        Assert.Equal(expectedSet, match.SetCode, ignoreCase: true);
        Assert.Equal(expectedNumber, match.CollectorNumber);
    }

    // --- Test infrastructure ---

    private class TestScryfallDbFactory(DbContextOptions<ScryfallDbContext> options)
        : IDbContextFactory<ScryfallDbContext>
    {
        public ScryfallDbContext CreateDbContext() => new(options);
    }

    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private class TestDataPathService(string dataDir) : IDataPathService
    {
        public string DataDirectory => dataDir;
        public string ScansDirectory => Path.Combine(dataDir, "scans");
        public string TempScansDirectory => Path.Combine(dataDir, "temp");
        public string SymbolsCacheDirectory => Path.Combine(dataDir, "symbols");
        public string LogsDirectory => Path.Combine(dataDir, "logs");
        public string? PendingDataDirectory => null;
        public bool IsMigrationPending => false;
        public void SetPendingDataDirectory(string path) { }
        public void CommitMigration() { }
        public void CancelPendingMigration() { }
    }
}
```

- [ ] **Step 3: Run the integration tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --filter "FullyQualifiedName~ScanMatchingIntegrationTests" -v normal
```

Expected: 18 test cases (9 cards x 2 DPI levels), all passing. Each test should output the matched card name and confidence in the test output.

- [ ] **Step 4: If any tests fail, diagnose and adjust**

If a scan doesn't match correctly at one DPI level, that's a legitimate finding — the matching pipeline has a weakness at that DPI. The test should still assert the correct expected card. If it fails:
- Check the Hamming distance (add diagnostic output)
- If the test DB is missing the card, re-run the sync tool
- If the match is genuinely wrong (pipeline limitation), mark that specific test case with `[Trait("Category", "KnownMismatch")]` and file an issue

- [ ] **Step 5: Run full test suite to check for regressions**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj -v normal
```

Expected: All tests pass — existing + new integration tests.

- [ ] **Step 6: Commit**

```bash
git add OmniCard.Tests/Services/ScanMatchingIntegrationTests.cs OmniCard.Tests/OmniCard.Tests.csproj
git commit -m "test: add scan matching integration tests with auto-discovery"
```

---

## Verification

After both tasks complete:

- [ ] **Verify adding a new image auto-creates a test case**

1. Copy a new card scan PNG (with the correct filename format) to the scan source folder
2. Re-run `sync-test-data.bat`
3. Run `dotnet test --filter ScanMatchingIntegrationTests`
4. Confirm the new image appears as an additional test case
