# Baseline Test Coverage Design

**Date:** 2026-07-10
**Branch:** anubisascends/issue3
**Goal:** Fill all untested service/class gaps to establish baseline code coverage across the codebase.

## Context

The existing test suite has ~45+ test files covering most services. However, several classes have zero or insufficient test coverage:

- **CollectionQueryService** — `GetLocationOverviewsAsync` (complex aggregation, in current branch changes)
- **MismatchLogService** — `LogMismatchAsync` (conditional logging)
- **CardArtCache** — LRU cache with file/HTTP image loading
- **SetSymbolCache** — SVG download/cache/render with rarity formatting
- **OptcgService** — Only correction tests exist; matching, search, price lookups untested
- **Web project** — ScanController, ScanHub, Razor pages (Index, Location, Card) entirely untested

## Approach

**Per-service gap fill** — one test class per untested service, introducing Moq for new tests while leaving existing hand-rolled fakes untouched.

## Infrastructure Changes

### Package Additions (`OmniCard.Tests.csproj`)
- `Moq` — mocking framework (new tests only)
- `Microsoft.AspNetCore.Mvc.Testing` — Web integration tests

### Project Reference Additions
- `OmniCard.Collection` — for CollectionQueryService, MismatchLogService
- `OmniCard.Web` — for WebApplicationFactory

### Test Data
- `OmniCard.Tests/TestData/` — test image files (JPEG/PNG) provided by user
- Configured as `<Content Include="TestData\**" CopyToOutputDirectory="PreserveNewest" />`

## Test Files

### 1. `Services/CollectionQueryServiceTests.cs`

**Target:** `CollectionQueryService.GetLocationOverviewsAsync`
**Dependencies:** Moq (IStorageContainerService, ICardService, ICardGameService) + SQLite in-memory

| Test | Description |
|------|-------------|
| `GetLocationOverviews_NoContainers_ReturnsEmpty` | Empty container list returns empty summaries |
| `GetLocationOverviews_ContainersWithNoCards_ReturnsZeroCounts` | Containers exist but no cards — count=0, values=0 |
| `GetLocationOverviews_CorrectCardCountAndPurchaseTotal` | Aggregates count and sum of PurchasePrice per container |
| `GetLocationOverviews_GameFilter_OnlyCountsMatchingGame` | With gameFilter=Mtg, OnePiece cards excluded |
| `GetLocationOverviews_MarketValue_UsesGameServicePrices` | Mocked GetCurrentPrices feeds into TotalMarketValue |
| `GetLocationOverviews_PriceDelta_CalculatesCorrectly` | Delta = market - purchase, percent = delta/purchase * 100 |
| `GetLocationOverviews_PriceDelta_ZeroPurchase_ZeroPercent` | No division by zero when PurchasePrice is 0 |
| `GetLocationOverviews_CoverImage_FromExplicitCoverCardId` | Container.CoverCardId resolves to that card's ImageUri |
| `GetLocationOverviews_CoverImage_FallbackToFirstCard` | No CoverCardId falls back to first card's ImageUri |

### 2. `Services/MismatchLogServiceTests.cs`

**Target:** `MismatchLogService.LogMismatchAsync`
**Dependencies:** SQLite in-memory

| Test | Description |
|------|-------------|
| `LogMismatch_HighConfidenceDifferentIds_PersistsLog` | Confidence=85, different IDs — row inserted |
| `LogMismatch_LowConfidence_DoesNotLog` | Confidence=79 — no row inserted |
| `LogMismatch_NullConfidence_DoesNotLog` | Confidence=null — no row inserted |
| `LogMismatch_SameGameSpecificId_DoesNotLog` | Same old/new ID — no row inserted |
| `LogMismatch_FieldsPopulatedCorrectly` | All MismatchLog fields match input CardMatch values |

### 3. `Services/OptcgServiceTests.cs`

**Target:** `OptcgService` (matching, search, prices, corrections)
**Dependencies:** Moq (IPerceptualHashService, IHttpClientFactory) + SQLite in-memory

| Test | Description |
|------|-------------|
| `FindClosestMatch_ExactCorrection_ReturnsMatch` | Hash in HashCorrections table returns that card |
| `FindClosestMatch_PHashDistance_ReturnsBestMatch` | Closest pHash distance card is returned |
| `FindClosestMatch_BeyondMaxDistance_ReturnsNull` | All candidates > maxDistance returns null |
| `SearchCards_Keyword_MatchesByName` | Plain text search finds cards by name substring |
| `SearchCards_Qualifier_FiltersBySet` | `set:OP01` filters to that set only |
| `GetCurrentPrice_ReturnsStoredPrice` | Looks up price from Prices JSON field |
| `RecordCorrection_PersistsToDb` | Hash+cardId written to HashCorrections table |

### 4. `Services/SetSymbolCacheTests.cs`

**Target:** `SetSymbolCache`
**Dependencies:** Moq (IHttpClientFactory, IDataPathService) + temp directory + [StaFact]

| Test | Description |
|------|-------------|
| `RegisterSetName_GetSetName_RoundTrip` | Register "m10"→"Magic 2010", retrieve by code |
| `GetSetName_UnknownCode_ReturnsNull` | Unregistered code returns null |
| `FormatRarityDisplay_AllRarities` | common→"Common", uncommon→"Uncommon", rare→"Rare", mythic→"Mythic Rare", unknown→"" |
| `GetSetSymbolAsync_UnsupportedRarity_ReturnsNull` | Rarity "special" not in map → null |
| `GetSetSymbolAsync_Downloads_AndCachesToDisk` | First call triggers HTTP, file saved to temp dir |
| `GetSetSymbolAsync_SecondCall_UsesCache_NoHttp` | Second call for same symbol does NOT hit HTTP |

### 5. `Services/CardArtCacheTests.cs`

**Target:** `CardArtCache`
**Dependencies:** Moq (IHttpClientFactory) + TestData images + [StaFact]

| Test | Description |
|------|-------------|
| `GetImage_NullPaths_ReturnsNull` | Both localPath and imageUri null → null |
| `GetImage_LocalFile_ReturnsBitmapImage` | Valid local file path loads image |
| `GetImage_SameKey_ReturnsCached` | Second call returns same instance (LRU hit) |
| `GetImage_HttpFallback_WhenLocalMissing` | No local file, imageUri set → HTTP load |
| `LruEviction_AtCapacity_RemovesOldest` | Fill to capacity+1, oldest entry evicted |
| `Evict_RemovesEntry` | Explicit evict, subsequent get returns fresh load |
| `Clear_EmptiesCache` | Clear sets Count to 0 |

### 6. `Web/ScanControllerTests.cs`

**Target:** `ScanController`
**Dependencies:** `WebApplicationFactory<Program>` or direct controller instantiation with Moq

| Test | Description |
|------|-------------|
| `Upload_ValidJpeg_Returns200WithSize` | POST valid JPEG file → 200 + size in response |
| `Upload_NoFile_Returns400` | POST with null image → 400 |
| `Upload_WrongContentType_Returns400` | POST text/plain → 400 error about JPEG/PNG |
| `Upload_OversizedFile_Returns400` | POST >10MB → 400 error about size limit |

### 7. `Web/WebPageTests.cs`

**Target:** `IndexModel`, `LocationModel`, `CardModel`
**Dependencies:** SQLite in-memory

| Test | Description |
|------|-------------|
| `IndexModel_OnGet_ReturnsContainersOrdered` | Containers sorted by SortOrder then Name |
| `LocationModel_OnGet_ReturnsCardsGroupedBySet` | Cards in container grouped into SetSummary list |
| `LocationModel_OnGet_NonexistentContainer_Returns404` | Unknown ID returns NotFound result |
| `CardModel_OnGet_ReturnsCardWithContainer` | Existing card ID returns card + included Container |
| `CardModel_OnGet_NonexistentCard_Returns404` | Unknown ID returns NotFound result |
| `CardModel_ImageUrl_ResolvesScanPath` | ScanImagePath set → `/scans/filename.jpg`; null → falls back to ImageUri |

## Testing Patterns

- **New tests use Moq** for interface dependencies
- **Existing hand-rolled fakes untouched** — no migration
- **SQLite in-memory** with shared connection for DB-backed tests (matches existing pattern)
- **[StaFact]** from Xunit.StaFact for WPF-dependent tests (CardArtCache, SetSymbolCache)
- **IDisposable** on test classes to clean up SQLite connections and temp files
- **TestData/** folder for real image files referenced by CardArtCache tests

## Total

**7 new test files, ~44 test cases**, covering all identified gaps in the codebase.
