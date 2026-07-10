using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

// --- Parse arguments ---
var scryfallDb = @"X:\TCG Card Scanner\scryfall.db";
var artDir = @"X:\TCG Card Scanner";
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
var cardKeys = new HashSet<(string Set, string Num)>();

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

Console.WriteLine($"Output directory: {testDataDir}");

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
