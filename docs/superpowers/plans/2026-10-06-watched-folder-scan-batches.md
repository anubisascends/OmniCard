# Watched-folder scan batches — implementation plan

**Branch:** `feat/scan-batches` · **Date:** 2026-10-06 · **Status:** planned

## Goal

A scanner saves card images into a per-game folder on the server, with one subfolder per batch. OmniCard
notices the new files, waits for a configurable quiet period (default 90 s) with no new files, and then
matches the batch in the background. Users open the app later, see a badge on **Scan**, claim a batch,
review it on the existing scan review screen, and commit it to a location. Several users can work on
different batches at the same time.

## Locked decisions

| Topic | Decision |
|---|---|
| Folder host | Local disk on the IIS server. Uses a `FileSystemWatcher` to wake up early, plus a periodic rescan and a scan at startup to catch anything the watcher missed. |
| Batch identity | `(game, top-level subfolder name)`. Files in the game root go to a batch named after the date (`yyyy-MM-dd`). Files in deeper subfolders belong to their top-level subfolder's batch. |
| Ingest | Copy into `{data}/scan-batches/{batchId}/`, then **move the original** to `<game folder>/_processed/<batch folder>/`. |
| Late files | **Appended** to the batch if it isn't Committed or Discarded. Otherwise a new batch named `Name (2)`, `Name (3)`, … |
| Multi-user | **Claim/lock.** Opening a batch claims it. Others can view it read-only. The owner or an admin can release it, and an admin can take it over. |
| Retention | Discarded items are deleted immediately. A closed batch's folder and rows are deleted after `RetentionDays` (default 14). |
| Default location | Optional per-folder default storage location, pre-selected at commit. |
| Badge | Counts **unclaimed** open batches. Collecting and Matching count as "in progress" and show a spinner on the panel. |
| Audit mode | Not supported for batches in v1. |
| Permissions | List, view and claim: `scan.view`. Commit: `scan.commit` plus site **write** access to the target location. Folder settings: **admin only**, because they point the server at arbitrary paths and move files there. |

## Architecture overview

```
 scanner ──► D:\Scans\Mtg\Box 12\*.jpg
                    │  FileSystemWatcher (wake) + 60 s rescan + startup scan
                    ▼
        ScanFolderIngestor ── stable? ──► copy → {data}/scan-batches/{id}/{itemId}.jpg
                    │                      move → D:\Scans\Mtg\_processed\Box 12\
                    ▼
        ScanBatch (Collecting) ─ quiet period elapsed ─► Matching ─ no Pending items ─► Ready
                    │                                         ▲
                    ▼                                         │ IScanMatcher.MatchAsync (one at a time)
        ScanBatchProcessor  ──────────────────────────────────┘
                    │
  SPA: badge (poll) ─► Batches panel ─► claim ─► /scan/batch/:id (ScanPage in batch mode)
                                                  edits PUT to server · commit → lots
```

One `BackgroundService` (`ScanBatchHostedService`) owns the loop and calls into two plain, testable
classes: `ScanFolderIngestor` and `ScanBatchProcessor`. The batch state machine and the claim/edit/commit
rules live in `ScanBatchService`, which the controller and the background service share. All of them
read the time from an injected `TimeProvider`.

---

## Phase 1 — Data model and settings

### 1.1 Entities — `OmniCard.Shared/Scanning/ScanBatch.cs`

```csharp
public enum ScanBatchStatus { Collecting, Matching, Ready, Committed, Discarded }
public enum ScanBatchItemStatus { Pending, Matched, Error }
public enum ScanBatchItemState { Open, Committed, Removed }

public class ScanBatch
{
    public int Id { get; set; }
    public CardGame Game { get; set; }
    public string Name { get; set; } = "";          // display name, e.g. "Box 12 (2)"
    public string FolderKey { get; set; } = "";     // source subfolder ("Box 12") or the date for root files
    public ScanBatchStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime LastFileUtc { get; set; }       // quiet-period anchor
    public DateTime? ReadyUtc { get; set; }
    public DateTime? ClosedUtc { get; set; }        // committed/discarded; retention anchor
    // Match settings, copied from the folder config when the batch is created (later config edits
    // don't change an existing batch).
    public bool IsFoil { get; set; }
    public string? Condition { get; set; }
    public string? Language { get; set; }
    public string? SetCodes { get; set; }           // comma-separated art-fallback sets
    public int? DefaultContainerId { get; set; }
    // Claim
    public int? ClaimedByUserId { get; set; }
    public string? ClaimedByName { get; set; }
    public DateTime? ClaimedUtc { get; set; }
}

public class ScanBatchItem
{
    public int Id { get; set; }
    public int ScanBatchId { get; set; }
    public int Sequence { get; set; }               // ingest order (stable display order)
    public string OriginalFileName { get; set; } = "";
    public string StoredFileName { get; set; } = ""; // "{Id}{ext}" inside the batch dir
    public string? PreviewFileName { get; set; }     // JPEG preview for TIFFs
    public ScanBatchItemStatus Status { get; set; }
    public ScanBatchItemState State { get; set; }
    public string? MatchJson { get; set; }           // serialized ScanMatchDto
    public string? OverrideJson { get; set; }        // serialized ScanSearchResultDto (user correction)
    public string? Error { get; set; }
    // Per-copy properties (mirror the SPA's ItemProps)
    public bool Include { get; set; }
    public bool Verified { get; set; }
    public string Condition { get; set; } = "NM";
    public string? Language { get; set; }
    public bool IsFoil { get; set; }
    public string? FoilType { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal? PurchasePrice { get; set; }
    public string? TagsJson { get; set; }
    public string? Note { get; set; }
}
```

The match and the correction are stored as JSON strings. `OmniCard.Shared` must not reference
`Api.Contracts`, and the DTO has about 18 fields that nothing ever queries. The web layer serializes them.

### 1.2 `OmniCardDbContext`

- Add `DbSet<ScanBatch> ScanBatches` and `DbSet<ScanBatchItem> ScanBatchItems`.
- `ScanBatch`: store `Game` and `Status` as strings. Indexes on `(Game, FolderKey, Status)` (ingest
  lookup) and `(Status, ClaimedByUserId)` (badge). `Name` max length 200, `FolderKey` max length 260.
- `ScanBatchItem`: FK to `ScanBatch` with **cascade delete**. Store `Status` and `State` as strings. Index
  on `(ScanBatchId, State, Status)`.
- Add **both** types to `ConcurrencyTrackedEntities`. The background processor and the claiming user can
  both write the same item: the processor writes match fields, the user writes properties. Each writer
  **loads, patches and saves** and retries once on `DbUpdateConcurrencyException` (see the CLAUDE.md
  gotcha about detached `Update()`).
- Migration: `dotnet ef migrations add AddScanBatches --project OmniCard.Web --startup-project OmniCard.Web`
  (the design-time factory is `OmniCard.Web/Data/DesignTimeOmniCardDbContextFactory.cs`).

### 1.3 Folder settings — follow the `ScanBadgeSettingsService` pattern

- `OmniCard.Shared/Settings/ScanFolderSettings.cs`:
  ```csharp
  public sealed class ScanFolderSettings
  {
      public bool Enabled { get; set; }                 // master switch (default off)
      public int QuietPeriodSeconds { get; set; } = 90; // clamped to 10..3600
      public int RetentionDays { get; set; } = 14;      // clamped to 1..365
      public List<ScanFolderConfig> Folders { get; set; } = [];
  }
  public sealed class ScanFolderConfig
  {
      public CardGame Game { get; set; }
      public string Path { get; set; } = "";
      public bool Enabled { get; set; } = true;
      public bool IsFoil { get; set; }
      public string Condition { get; set; } = "NM";
      public string? Language { get; set; }
      public List<string> SetCodes { get; set; } = [];
      public int? DefaultContainerId { get; set; }
  }
  ```
- `IScanFolderSettingsService` (`Get()`, `Save(ScanFolderSettings)`, `event Action? Changed`), stored in
  `scan-folder-settings.json`. **Validate on save:**
  - at most one folder per game;
  - paths must be rooted and must not overlap (no folder inside another);
  - a path must not be inside the data directory;
  - drop any game that no registered `ICardGameService` provides.

  A path that doesn't exist is allowed but gets flagged. The status endpoint reports it, so the admin can
  configure the folder before creating it.
- Register it as a singleton in `Program.cs` next to `IScanBadgeSettingsService`.

---

## Phase 2 — Background pipeline

### 2.1 `IScanMatcher` seam

Extract `IScanMatcher { Task<ScanMatchDto> MatchAsync(byte[], CardGame, bool, IReadOnlyCollection<string>?, string?, CancellationToken); }`
from `WebScanMatchingService` (the method signature stays the same). Register the concrete singleton under
both types. The processor depends on the interface, so tests can fake matching. `CardScanController`
keeps using the concrete type.

### 2.2 `ScanFolderIngestor` — `OmniCard.Web/Services/ScanBatches/`

`RunOnce(ScanFolderSettings settings)` is called by the hosted service on every tick and on every wake:

1. Enumerate each enabled folder recursively, skipping any directory whose name starts with `_` or `.`
   (this covers `_processed`). Keep `.jpg/.jpeg/.png/.tif/.tiff` files and reuse
   `CardScanController.IsAcceptedImage`, moving it to a shared helper. Skip hidden, system and `~$*` or
   `*.tmp` files.
2. **Stability gate.** Keep an in-memory `Dictionary<path, (long size, DateTime lastWrite, DateTime seenUtc)>`.
   A file is ready when:
   - its size and last-write time haven't changed since a sighting at least 3 s earlier; **and**
   - it opens with `FileShare.None`, which proves the scanner software has released it.

   A file that fails stays tracked and is retried on the next tick. After a restart the dictionary is
   empty, so every file is seen twice before it's taken in. That costs one tick of delay and is
   harmless.
3. **Batch lookup.** Find the open batch (`Status ∉ {Committed, Discarded}`) for `(Game, FolderKey)`. If
   there isn't one, create a batch:
   - `Name` = the folder key, plus ` (n)` when a closed batch already used that name;
   - match settings copied from the folder config;
   - `Status = Collecting`.
4. **Take in the file:**
   - insert a `Pending` `ScanBatchItem` to get its `Id`;
   - copy the file to `{data}/scan-batches/{batchId}/{itemId}{ext}`;
   - save `StoredFileName` and seed `Condition`, `Language` and `IsFoil` from the batch;
   - move the original to `<root>/_processed/<FolderKey>/<file>`, adding ` (n)` to the name if one is
     already there;
   - set `batch.LastFileUtc = now`. If the batch was `Ready`, set it back to `Matching`. Late files are
     matched straight away, without a second quiet period.

   **Failure rules:**
   - If the copy fails, delete the item row and retry the file on the next tick.
   - If the move fails after a successful copy, keep the item. Record the source path in a "moved
     failed" set so the file isn't taken in twice, and retry the move on the next tick.
   - Log a warning and continue with the next file.
5. Leave emptied subfolders alone. The scanner may still be writing into one, and the append rule
   depends on that.

### 2.3 `ScanBatchProcessor`

`ProcessNextAsync(ct)`:

1. **Promote:** `Collecting` batches whose `LastFileUtc + QuietPeriod <= now` become `Matching`.
2. Pick the **oldest** `Matching` batch that still has `Pending` + `Open` items, and take its next
   item by `Sequence`. If no item is left, set the batch to `Ready` with `ReadyUtc = now`.
3. Read the file, then call
   `matcher.MatchAsync(bytes, batch.Game, item.IsFoil, batch.SetCodes, batch.Language)`.
   - Then set `IsNew` through `binderCards.IsNewCard`, which `CardScanController.Match` already does.
   - For a TIFF, write a JPEG preview `{itemId}.preview.jpg` with the existing `RenderPreviewDataUri`
     helper refactored to return bytes. Previews are stored as files, not data URIs in the database.
   - Load-patch the item:
     - `MatchJson`, `Status = Matched`, `Include = match.Matched`;
     - `Language = match.Language` when the match returned one, which is the same seeding the SPA does.

   **On an exception:** set `Status = Error` and `Error = message`, so the item can't block the batch.
4. Process one item per call. The hosted loop calls it until there's no work left, which leaves room
   for cancellation and keeps background CPU at **one matching thread**. Interactive scans run up to 8 in
   parallel, so they're barely affected. `_matchGate` already makes sure the two can't share a database
   context at the same time.

**Restart recovery** needs no extra code. Nothing is stored as "in flight", so a `Pending` item is simply
picked up again after a restart.

### 2.4 `ScanBatchHostedService : BackgroundService` — the first one in the app

- On start, and whenever `Changed` fires, (re)create a `FileSystemWatcher` for each enabled folder:
  - `IncludeSubdirectories = true`, notify on `FileName | Size | LastWrite`;
  - `InternalBufferSize = 64 KB`;
  - the `Created`, `Changed`, `Renamed` and `Error` events all just release a `SemaphoreSlim` to wake
    the loop. The handlers do no work themselves.
- Loop:
  - wait on that semaphore or a **5 s** timer, whichever comes first;
  - run `ingestor.RunOnce` every 60 s, and also whenever a watcher event or pending unstable files need
    re-checking;
  - drain `processor.ProcessNextAsync` until it reports no work;
  - run the retention sweep once an hour.
- The ingestor runs only while `settings.Enabled`. The processor always runs, so batches that were
  already taken in still finish.
- Wrap each phase in try/catch and log, so one bad file or folder never kills the loop.
- Register it with `builder.Services.AddHostedService<ScanBatchHostedService>()`.

### 2.5 Retention sweep — `ScanBatchService.PurgeExpired(now)`

Delete the batches where `ClosedUtc < now - RetentionDays`: first the folder under `scan-batches/`, then
the row (its items cascade).

---

## Phase 3 — Service and API

### 3.1 `ScanBatchService` — `OmniCard.Web/Services/ScanBatches/`, singleton, uses `WritableOmniCardDbContextFactory`

| Method | Rules |
|---|---|
| `List(userId)` | Every batch that isn't closed, plus batches closed in the last 24 h, as summary DTOs with counts. |
| `CountUnclaimed()` | `Status ∈ {Collecting, Matching, Ready} && ClaimedByUserId == null`. |
| `Get(id)` | The batch and its `Open` items. |
| `Claim(id, user, force)` | **Atomic:** `ExecuteUpdate … WHERE Id=@id AND (ClaimedByUserId IS NULL OR ClaimedByUserId=@me)`. `force` (admin only) drops the second condition. Zero rows updated → 409 with the current owner's name. |
| `Release(id, user, isAdmin)` | Owner or admin. |
| `SaveItems(id, user, edits[])` | Caller must hold the claim (else 409). Load-patch only the property fields and `OverrideJson`; never `MatchJson` or `Status`. Unknown or non-open ids are ignored. |
| `RemoveItems(id, user, ids)` | Owner only. Set `State = Removed` and delete the image files right away. |
| `Rematch(id, user, ids)` | Owner only. Reset to `Pending` (used for "retry errors"). If the batch is `Ready`, set it back to `Matching`. |
| `Commit(id, user, containerId, ids)` | Owner only. Build `ScanCommitItem`s **from the server copy of the items**: the override wins over the match, the same rule as `toCommitItem` on the client. Hand them to the shared commit core (3.2) and mark the items `Committed`. If no `Open` items remain, set the batch to `Committed`, set `ClosedUtc`, and clear the claim. |
| `Discard(id, user, isAdmin)` | Owner or admin. Remove every open item and its file, set `Status = Discarded`, and set `ClosedUtc`. |

### 3.2 Extract a commit core

Move the body of `CardScanController.Commit` into `ScanCommitService.Commit(containerId, items)`. That
body is: `MapScanItem` → `AddScannedLots` → tags → `RecordScanCorrections`. The interactive endpoint and
the batch commit both call it, so pHash correction learning and the deck-box game guard apply to both.
`MapScanItem` and `RecordScanCorrections` move with it. The controller's existing tests must keep
passing unchanged.

### 3.3 `ScanBatchesController` — `[ApiAuth][Route("api/scan/batches")]`

| Verb and route | Gate |
|---|---|
| `GET /` → `ScanBatchSummaryDto[]` | `scan.view` |
| `GET /count` → `{ unclaimed }` | `scan.view` |
| `GET /{id}` → `ScanBatchDto` (summary + `ScanBatchItemDto[]`) | `scan.view` |
| `GET /{id}/items/{itemId}/image` → the file (the preview JPEG for a TIFF) | `scan.view` |
| `POST /{id}/claim?force=` | `scan.view` (`force` needs admin) |
| `POST /{id}/release` | `scan.view` |
| `PUT /{id}/items` (body `ScanBatchItemEdit[]`) | `scan.view` + claim |
| `POST /{id}/items/remove` (ids) | `scan.view` + claim |
| `POST /{id}/items/rematch` (ids) | `scan.view` + claim |
| `POST /{id}/commit` (`{ containerId, itemIds }`) | `scan.commit` + `[RequireSiteAccess(Write, Location = "ContainerId")]` + claim |
| `POST /{id}/discard` | `scan.commit` + claim, or admin |

Notes:

- Images are served through a controller, **not** static files, so they stay behind the login and the
  permission check. Set `Cache-Control: private, max-age=86400`; an item's file never changes.
- The user id and name come from `AppAuthGate.CurrentUserId/CurrentUsername`. Claim conflicts return 409
  with `{ error, claimedBy }`.
- Folder settings (admin):
  - `GET /api/settings/scan-folders` returns the settings plus a per-folder `{ exists, writable, lastError }`
    status. It is gated with `[ApiAuth(RequireAdmin = true)]`.
  - `PUT /api/settings/scan-folders` returns 400 with validation messages.

### 3.4 DTOs — `OmniCard.Api.Contracts/Dtos.cs`

- `ScanBatchSummaryDto`: `Id, Name, Game, Status, Total, Pending, Matched, Errors, Committed,
  ClaimedBy, ClaimedByMe, CreatedUtc, LastFileUtc, ReadyUtc, DefaultContainerId`.
- `ScanBatchItemDto`: `Id, Sequence, FileName, ImageUrl, Status, Match (ScanMatchDto?),
  Override (ScanSearchResultDto?), Error, Include, Verified, Condition, Language, IsFoil, FoilType,
  Quantity, PurchasePrice, Tags, Note`.
- `ScanBatchDto`: summary + `SetCodes` + `Items`.
- `ScanBatchItemEdit`: `Id` + the editable fields.
- `ScanBatchCommitRequest(ContainerId, ItemIds)`.
- `ScanFolderSettingsDto` and `ScanFolderStatusDto`.

---

## Phase 4 — SPA

### 4.1 API client and types — `src/api/client.ts`, `src/api/types.ts`

Add `scanBatches`, `scanBatchCount`, `scanBatch(id)`, `scanBatchClaim/Release`, `scanBatchSaveItems`,
`scanBatchRemove`, `scanBatchRematch`, `scanBatchCommit`, `scanBatchDiscard`, `scanFolderSettings` and
`scanFolderSettingsUpdate`.

### 4.2 Nav badge — `src/components/AppShell.tsx`

- Add an optional `badge?: 'scanBatches'` to the `NAV` entry for `/scan`.
- When the user has `scan.view`, run `useQuery(['scan-batches-count'], api.scanBatchCount,
  { refetchInterval: 20_000, refetchIntervalInBackground: false })`.
- Wrap the icon in MUI `<Badge badgeContent={n} color="secondary" max={99}>`. Nothing is shown at 0.
- Invalidate the count after claim, release, commit and discard.

### 4.3 Batches panel — `src/pages/scan/ScanBatchesPanel.tsx` (new)

- Rendered at the top of `ScanPage`, only in plain scan mode (not audit, not batch). It is a collapsible
  card that appears only when there are batches.
- It polls `api.scanBatches` every 10 s while any batch is Collecting or Matching, and otherwise every 30 s.
- Each row shows:
  - the game icon/label and the name;
  - a status chip: *Waiting for files* / *Matching x of y* (with `LinearProgress`) / *Ready* /
    *Committed*;
  - "In review by {{name}}", or **Open**, which claims the batch and navigates;
  - admin actions: **Release** and **Discard**.
- Ages are formatted with `fmt.dateTime`.

### 4.4 Batch review route — `/scan/batch/:batchId`

- Add a `src/pages/ScanBatchPage.tsx` wrapper, in the same style as `AuditPage`. It loads the batch and
  claims it if it's unclaimed, then renders `<ScanPage batch={…} readOnly={!claimedByMe} />`.
- Register the route in `App.tsx` behind `scan.view`.

### 4.5 `ScanPage` batch mode — the main refactor

1. **Store abstraction.** Extract `interface ScanItemStore<T> { load(): Promise<{game, items}|null>; sync(game, items): Promise<void> }`.
   - `ScanSessionStore` (IndexedDB) already has this shape.
   - Add `BatchItemStore` (`src/lib/batchItemStore.ts`). Its `load` maps `ScanBatchItemDto` to `ScanItem`.
     Its `sync` diffs by object identity, the same way `ScanSessionStore` does, and sends `PUT` for the
     changed items only. The 400 ms debounce is kept, and changes are flushed before a commit.
2. Make `ScanItem.file` optional. Batch items have no local `File`; `previewUrl` is the server
   `ImageUrl`. Guard the `isTiff(it.file)` and `api.scanMatch(staging.file, …)` call sites.
3. **In batch mode:**
   - the game is locked to `batch.game`;
   - the art-set, foil, language and condition seed controls are hidden, and a read-only summary of the
     batch settings is shown instead;
   - the add-images, webcam and take-photo inputs are hidden;
   - nothing is matched client-side;
   - the location defaults to `batch.defaultContainerId`.
4. **Live progress.** While the batch has `Pending` items, poll `api.scanBatch(id)` every 3 s. Copy the
   server's `status`, `match`, `error`, `include` and `language` **only into items that are still
   `matching` locally**, so the poll never overwrites the user's edits. Merge in new items from late
   files.
5. **Commit and remove.**
   - Commit calls `scanBatchCommit(id, containerId, keys→ids)`. On success, the committed items are
     dropped from the list.
   - When the batch closes, show a summary and navigate back to `/scan`.
   - Remove calls `scanBatchRemove`.
   - A new **Retry errors** button calls `scanBatchRematch`.
6. **Read-only mode** (the batch is claimed by someone else):
   - a banner "In review by {{name}}", with **Take over** for admins (`claim?force=true`);
   - every edit, commit and remove control is disabled;
   - nothing is synced.
7. **Losing the claim.** Any 409 from a save or commit means the claim was lost. Show a banner and
   switch to read-only.

### 4.6 Settings ▸ Scan — `ScanFoldersCard` in `SettingsPage.tsx`

- Shown only to admins, under the existing `scan` tab.
- **Global:** enable switch, quiet period (seconds), retention (days).
- **Per game** (one row per available game):
  - path text field, with a status chip: *Found* / *Missing* / *No write access*;
  - enabled switch;
  - foil, condition and language selects;
  - an art-sets picker that reuses the scan page's set picker (extract it if it's local to the page);
  - a default location picker, using `LocationPickerDialog` with `allowCreate={false}`.
- Show the server's 400 validation messages inline.

### 4.7 i18n

- **`scan.json` → `scan.batches.*`:** panel title, status labels, "In review by {{name}}", open,
  release, discard (with a confirm), take over, retry errors, read-only banner, claim-lost banner,
  batch-settings summary, empty state, and "closed batch" summary.
- **`settings.json` → `settings.scanFolders.*`:** every field label and help text, plus the status chips.

No hard-coded strings. Numbers, dates and progress text go through `useFormatters()`.

---

## Phase 5 — Tests (`OmniCard.Tests/Web/ScanBatches/`)

They use in-memory SQLite plus a temp directory (deleted in `Dispose`), and a hand-rolled
`FakeTimeProvider : TimeProvider` (no new package).

- **ScanFolderIngestorTests**
  - a subfolder becomes a batch named after it, and root files go to a date batch;
  - a non-image file is ignored, and `_processed` and `_*` folders are skipped;
  - a growing file isn't taken in until it is stable; a file held open with `FileShare.None` is skipped;
  - the original moves to `_processed/<key>/`, and a name collision gets a suffix;
  - a late file is appended to an open batch, and a `Ready` batch goes back to `Matching`;
  - after the batch is committed, a new batch `Name (2)` is created;
  - a failed copy leaves no item row.
- **ScanBatchProcessorTests** (fake `IScanMatcher`)
  - a batch stays `Collecting` until the quiet period elapses;
  - items are matched in sequence order, then the batch becomes `Ready`;
  - an exception marks the item `Error` and the batch still finishes;
  - `Pending` items are picked up after a "restart" (a new processor instance);
  - a TIFF gets a preview file.
- **ScanBatchServiceTests**
  - claim is exclusive (the second user gets a conflict) and admin force works;
  - release is allowed for the owner or an admin;
  - a non-owner's save is rejected;
  - a save never touches `MatchJson`;
  - a partial commit creates lots and leaves the batch open, and the last commit closes it and clears
    the claim;
  - discard deletes the files;
  - `PurgeExpired` removes old closed batches only;
  - `CountUnclaimed` is correct.
- **ScanFolderSettingsServiceTests**
  - overlapping paths are rejected, as are paths inside the data directory and duplicate games;
  - values are clamped;
  - a corrupt file falls back to the defaults.
- **CardScanControllerTests**: unchanged and still green after the commit-core extraction.
- **Manual:** against live SQL Server and IIS:
  - rowversion retries;
  - the app pool's file-system permissions;
  - a real scanner drop of 50+ TIFFs.

---

## Phase 6 — Docs and housekeeping

- `OmniCard.Web/README.md`: a new **Watched scan folders** section covering:
  - setup in Settings ▸ Scan;
  - the folder layout (`<game>\<batch>\…`, `_processed`);
  - the quiet period, append and retention behavior;
  - claim/lock.
- Also add an **IIS always-on** subsection, because otherwise nothing watches the folders while the app
  is idle:
  - app pool `startMode="AlwaysRunning"`, `idleTimeout="00:00:00"` (and consider disabling periodic
    recycling);
  - site/application `preloadEnabled="true"` (requires the *Application Initialization* IIS feature);
  - grant `IIS AppPool\<name>` **Modify** on each watch folder, so files can be moved to `_processed`.
- Add `scan-batches/` to the list of data-directory folders in the IIS permissions step.
- Update `CLAUDE.md`'s Architecture section with a short "Scan batches" paragraph: the first
  BackgroundService, where the pieces live, and server-side persistence vs the IndexedDB browser
  sessions.
- No new NuGet or npm packages are expected, so no changes to `THIRD-PARTY-NOTICES.txt`.

---

## Suggested commit sequence

1. `feat(scan-batches): entities, migration, folder settings service` (+ tests)
2. `refactor(scan): extract IScanMatcher + ScanCommitService` (existing tests green)
3. `feat(scan-batches): ingestor + processor + hosted service` (+ tests)
4. `feat(scan-batches): ScanBatchService + API` (+ tests)
5. `feat(spa): nav badge + batches panel + settings card`
6. `feat(spa): ScanPage batch mode (BatchItemStore, live progress, claim/read-only)`
7. `docs: README watched folders + IIS always-on; CLAUDE.md`

## Risks and open points

- **`ScanPage.tsx` is 2,000 lines.** Batch mode adds another mode branch. Keep the new behavior in
  `BatchItemStore` plus a small `useBatchSync` hook to limit the inline conditionals. Splitting the page
  up is out of scope.
- **FileSystemWatcher reliability.** It's only a wake signal; correctness comes from the 60 s rescan, so
  buffer overflows and missed events are harmless.
- **Single app instance assumed.** Two IIS instances or a web garden would both run the ingestor. That
  matches the current deployment, but the README should say so.
- **Large batches.** `GET /{id}` returns every item. That's fine for hundreds. If batches reach the
  thousands, add `?changedSince=` later.
- **Disk.** The originals stay in `_processed`, so each scan is kept twice until retention clears the
  batch copy. Clearing `_processed` is left to the user (documented).
