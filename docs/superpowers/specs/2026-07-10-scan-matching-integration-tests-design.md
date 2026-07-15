# Scan Matching Integration Tests Design

**Date:** 2026-07-10
**Branch:** feat/manabox-scan-export (or new branch)
**Goal:** Create integration tests that verify the pHash + matching pipeline produces correct card identifications from real scanned card images, with a bat file to sync test data from external sources.

## Context

The matching pipeline (scan image → pHash → ScryfallService.FindClosestMatch → CardMatch) is the core of the app but has no end-to-end integration test. Unit tests exist for individual stages (pHash computation, tie-zone scoring, etc.) but nothing verifies that a real scanned image resolves to the correct card.

Test images are provided at two DPI levels (200 and 1200) in a configurable source directory. Reference card data (hashes, art files) lives in the user's `scryfall.db` and `art/` folder on `X:\TCG Card Scanner`.

## Components

### 1. Data Sync Tool

**`OmniCard.Tests/Tools/SyncTestData/SyncTestData.csproj`** — a minimal .NET console app.

**Arguments:**
- `--scryfall-db <path>` — default: `X:\TCG Card Scanner\scryfall.db`
- `--art-dir <path>` — default: `X:\TCG Card Scanner\art`
- `--scan-source <path>` — default: `C:\Users\anubi\OneDrive\Desktop\Test_Data`

**Behavior:**
1. Recursively scans `<scan-source>` for `*.png` files
2. Parses each filename with regex: `(?<name>.+) \[(?<set>[A-Za-z0-9]+)\] #(?<num>[^\.\s]+)\.png` to extract set code and collector number
3. Deduplicates by (set code, collector number) — same card at different DPIs is one DB query
4. Opens `<scryfall-db>` read-only
5. Creates `OmniCard.Tests/TestData/test-scryfall.db`:
   - `Cards` table with only the matched rows (all columns preserved, including ImageHash, ArtHash, LocalImagePath, Prices JSON, ImageUris JSON, etc.)
   - `HashCorrections` table (empty, schema only)
   - `SetSymbolHashes` table (empty, schema only)
   - `RelatedCards` table (empty, schema only)
6. For each matched card, copies its art file: `<art-dir>/<LocalImagePath>` → `OmniCard.Tests/TestData/art/<setcode>/<number>.jpg` (preserving the path structure the service expects)
7. Copies all scan image subfolders from `<scan-source>` to `OmniCard.Tests/TestData/scans/` (e.g., `200 dpi/`, `1200 dpi/`)
8. Prints summary: `Synced {N} cards, {N} art files, {N} scan images from {source}`

**The tool uses `Microsoft.Data.Sqlite` directly** — no EF dependency. It creates the target DB schema via `CREATE TABLE` SQL matching the ScryfallDbContext schema, then `INSERT` rows from the source DB.

### 2. Bat File

**`OmniCard.Tests/TestData/sync-test-data.bat`**

```bat
@echo off
REM Sync test data from external sources into TestData/
REM Usage: sync-test-data.bat [--scryfall-db path] [--art-dir path] [--scan-source path]
dotnet run --project "%~dp0..\Tools\SyncTestData\SyncTestData.csproj" -- %*
```

Runs the sync tool with any passed-through arguments. Default paths work for the user's machine without arguments.

### 3. Test Data Directory Structure

After running the bat file:

```
OmniCard.Tests/TestData/
├── test-scryfall.db              # Subset Scryfall DB with matched cards only
├── art/                          # Art images for hash comparison
│   ├── cmm/
│   │   ├── 64.jpg
│   │   ├── 251.jpg
│   │   └── ...
│   ├── m21/
│   │   └── 220.jpg
│   └── ...
├── scans/                        # Scanned card images at various DPIs
│   ├── 200 dpi/
│   │   ├── Beanstalk Giant [CMM] #275.png
│   │   ├── Filigree Attendant [CMM] #95.png
│   │   └── ...
│   └── 1200 dpi/
│       ├── Beastalk Giant [CMM] #275.png
│       └── ...
└── sync-test-data.bat
```

All contents of `TestData/` are committed to the repo. The bat file is re-run manually when:
- New test images are added to the scan source folder
- `scryfall.db` is updated with new data/hashes

### 4. Integration Test

**`OmniCard.Tests/Services/ScanMatchingIntegrationTests.cs`**

**Constructor setup:**
- Copies `TestData/test-scryfall.db` to a temp path (avoids file locking on the committed file)
- Creates `ScryfallDbContext` via SQLite pointing to the temp copy
- Constructs a real `PerceptualHashService` (with `NullLogger`)
- Constructs a real `ScryfallService` with the test DB context factory, the real hash service, a stub `IHttpClientFactory`, and `NullLogger`
- The `ScryfallService` lazily builds its in-memory hash cache from the test DB on the first `FindClosestMatch` call

**Test discovery (auto-discovers all scan images):**
- A static `[MemberData]` method globs `TestData/scans/**/*.png`
- Each file becomes a test case with parameters: `(string relativePath, string expectedSetCode, string expectedCollectorNumber)`
- Set code and collector number are parsed from the filename regex
- Both DPI folders are discovered automatically — each card gets a test case per DPI level
- Adding new images to `TestData/scans/` (via the bat file) automatically creates new test cases with no code changes

**Test assertion per image:**
1. Load the PNG file as a `FileStream`
2. Call `PerceptualHashService.ComputeHash(stream)` → `ulong hash`
3. Call `ScryfallService.FindClosestMatch(hash)` → `CardMatch?`
4. Assert: match is not null
5. Assert: `match.SetCode` equals `expectedSetCode` (case-insensitive)
6. Assert: `match.CollectorNumber` equals `expectedCollectorNumber`
7. Output diagnostic info: confidence, Hamming distance, matched name (via `ITestOutputHelper`)

**xUnit attributes:**
- `[Theory]` with `[MemberData]` for parameterized discovery
- No `[StaFact]` needed — `PerceptualHashService.ComputeHash` uses `System.Drawing.Bitmap`, not WPF types

**csproj addition:**
```xml
<Content Include="TestData\**" CopyToOutputDirectory="PreserveNewest" />
```

### 5. Test Tool Project

**`OmniCard.Tests/Tools/SyncTestData/SyncTestData.csproj`** — a standalone console app:
- Targets `net10.0` (no Windows TFM needed — just SQLite + file I/O)
- References `Microsoft.Data.Sqlite` only
- NOT referenced by the test project — it's a standalone tool invoked by the bat file
- NOT included in the solution file (optional — it's a developer tool)

## Out of Scope

- No OCR testing (OCR requires Windows.Media.Ocr runtime, adds complexity)
- No art hash matching tests (the basic pHash → match flow is the target)
- No auto-crop testing (test images are already cropped card scans)
- No changes to production code
- No CI integration for the bat file (it's a manual developer step)
