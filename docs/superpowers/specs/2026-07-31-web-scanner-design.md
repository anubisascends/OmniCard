# Design: Mobile Web Camera Card Scanner

Implements `docs/PRD/WebScanner.md`.

## Context

Most of the plumbing for this feature already exists:

- `OmniCard.Web/Pages/Scan.cshtml` is a working mobile camera viewfinder with client-side card-edge/gradient auto-crop, pinch-zoom, and a non-HTTPS `<input capture="environment">` fallback.
- `OmniCard.Web/Api/ScanController.cs` (`POST /api/scan`) and `OmniCard.Web/Hubs/ScanHub.cs` already relay the captured image from the phone to the desktop app over SignalR.
- `OmniCard/Services/WebScannerService.cs` runs inside the desktop app, connects out to Web's `/hubs/scan` as a SignalR client, and feeds the image straight into `OmniCard.Collection.CardService.AddFromStream` — the exact same pipeline a TWAIN scan uses (pHash + art-hash + foil edge-hash + OCR matching via `ICardGameService.FindClosestMatch`).

**The gap:** nothing reports the match result back to the phone. The phone currently just shows "Sent!" — no success screen, no card art, no correction path, and no way to pick which game a scan should match against. This design closes that loop using components that already exist wherever possible.

## Decisions (confirmed with user)

- **Result delivery:** synchronous round trip. `POST /api/scan` blocks while Web invokes a SignalR "client result" (`IHubContext<T>.Clients.Client(id).InvokeAsync<TResult>(...)`, supported outside a Hub method since .NET 7/9) on the desktop's existing hub connection, and returns the match in the same HTTP response. No phone-side SignalR client, no polling, no in-memory job store.
- **Commit behavior:** unchanged from today. A phone scan lands in the desktop's existing `ScannedCards` review queue exactly like a TWAIN scan — nothing is auto-committed to inventory, no DB schema changes. The phone only shows a preview/confirmation of the match.
- **Correction scope:** catalog search by name via the existing `ICardGameService.SearchCards` (read-only, same mechanism the desktop's card-editor reassignment search uses), scoped to the active game. Not a search over the owned collection.
- **Game selection:** the phone gets its own game picker (MTG/OPTCG/Riftbound/Pokémon/Yu-Gi-Oh!/FFTCG), sent with each upload. It does **not** reuse/mutate the desktop's shared toolbar `SelectedGame` toggle, since that would risk cross-talk with a concurrent TWAIN scan session on the desktop.

## Data flow

```
Phone (Scan.cshtml)
  1. Pick game (new picker, persisted in localStorage) → capture/crop photo (existing, unchanged)
  2. POST /api/scan  { image, game }
       |
ScanController (Web)
  3. Look up the tracked desktop SignalR connection id (new: ScanHub tracks it on connect/disconnect)
  4. await hubContext.Clients.Client(desktopId).InvokeAsync<ScanResultDto>("ProcessScan", image, game, ct)
     (~15s timeout)
  5. Return ScanResultDto as the HTTP response
       | (over the existing desktop -> Web hub connection)
WebScannerService (Desktop)
  6. hubConnection.On<byte[], CardGame, ScanResultDto>("ProcessScan", ...) replaces today's
     fire-and-forget "ImageReceived" handler
  7. Calls CardService.AddFromStream(stream, gameOverride) — signature grows a game override
     param and now returns the ScannedCard instead of void. Hash/match computation is unchanged;
     it already happens synchronously before the existing Dispatcher.BeginInvoke that queues the
     card for desktop review.
  8. Maps the resulting ScannedCard -> ScanResultDto, returned as the handler's result
       |
Phone shows result screen: matched -> art + name/set/number; no match -> straight to correction search.
```

**Correction path** (mismatch or no-match): phone shows a search box -> `GET /api/scan/search?game=X&q=...` calls Web's own read-only `ICardGameService.SearchCards` directly (no desktop round trip needed). User picks a result -> `POST /api/scan/correct { scanHash, game, correctCardId }` -> relayed to desktop the same InvokeAsync way, where desktop calls `RecordCorrection` (needs write access, hence must go through desktop — Web's DBs are opened read-only) and patches the matching `ScannedCard` in its queue by hash.

## Backend changes

**`OmniCard.Shared/Models/ScanResultDto.cs`** (new) — shared between Web and desktop:

```csharp
public class ScanResultDto
{
    public bool Matched { get; init; }
    public string? Name { get; init; }
    public string? SetName { get; init; }
    public string? SetCode { get; init; }
    public string? CollectorNumber { get; init; }
    public string? Rarity { get; init; }
    public string? ImageUri { get; init; }
    public double? Confidence { get; init; }
    public ulong ScanHash { get; init; }   // needed later for /api/scan/correct
    public CardGame Game { get; init; }
    public string? Error { get; init; }    // e.g. "Desktop not connected"
}
```

**`OmniCard.Collection/CardService.cs`** — `AddFromStream` gains an optional game override and a return value:

```csharp
public ScannedCard AddFromStream(Stream stream, CardGame? gameOverride = null)
```

Every `SelectedGame` read inside the method body becomes `var game = gameOverride ?? SelectedGame;`. Matching stays single-game (`FindBestMatch` only ever probes one game service) — the override just substitutes which game that is for this one call, without touching the shared desktop toolbar toggle. Existing TWAIN callers keep calling it with no override and can ignore the return value (source-compatible change).

**`OmniCard.Web/Hubs/ScanHub.cs`** — track the connected desktop's connection id (single-desktop assumption, matches the existing `Clients.All` broadcast pattern being replaced):

```csharp
public class ScanHub : Hub
{
    public override Task OnConnectedAsync() { DesktopConnectionTracker.Set(Context.ConnectionId); ... }
    public override Task OnDisconnectedAsync(Exception? ex) { DesktopConnectionTracker.Clear(Context.ConnectionId); ... }
}
```

`DesktopConnectionTracker` is a small singleton service (last-connected wins) injected into `ScanController`.

**`OmniCard.Web/Api/ScanController.cs`** — `Upload` becomes request/response instead of fire-and-forget broadcast; add `Search` and `Correct`:

```csharp
[HttpPost] // POST /api/scan
public async Task<IActionResult> Upload(IFormFile image, CardGame game, CancellationToken ct)
{
    var connId = _tracker.CurrentConnectionId;
    if (connId is null)
        return Ok(new ScanResultDto { Matched = false, Error = "Desktop app not connected" });

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(15));
    try
    {
        var result = await _hubContext.Clients.Client(connId)
            .InvokeAsync<ScanResultDto>("ProcessScan", imageBytes, game, cts.Token);
        return Ok(result);
    }
    catch (OperationCanceledException)
    {
        return Ok(new ScanResultDto { Matched = false, Error = "Desktop app timed out" });
    }
}

[HttpGet("search")] // GET /api/scan/search?game=X&q=...
public IActionResult Search(CardGame game, string q)
    => Ok(_gameServices[game].SearchCards(q, 20));

[HttpPost("correct")] // POST /api/scan/correct
public async Task<IActionResult> Correct(ulong scanHash, CardGame game, string correctCardId, CancellationToken ct)
{
    var connId = _tracker.CurrentConnectionId;
    if (connId is null) return BadRequest(new { error = "Desktop app not connected" });
    await _hubContext.Clients.Client(connId).InvokeAsync<bool>("RecordCorrection", scanHash, game, correctCardId, ct);
    return Ok();
}
```

Existing content-type/size validation on `Upload` is retained.

**`OmniCard/Services/WebScannerService.cs`** — replace the `.On<byte[]>("ImageReceived", ...)` fire-and-forget handler with two client-result handlers:

```csharp
_hubConnection.On<byte[], CardGame, ScanResultDto>("ProcessScan", async (imageData, game) =>
{
    using var stream = new MemoryStream(imageData);
    var scanned = _cardService.AddFromStream(stream, game);
    return MapToDto(scanned); // ScanResultDto built from scanned.Match (may be null) + scanned.Hash + game
});

_hubConnection.On<ulong, CardGame, string, bool>("RecordCorrection", (hash, game, correctCardId) =>
{
    _cardService.GetGameService(game).RecordCorrection(hash, correctCardId);
    var scan = _cardService.ScannedCards.FirstOrDefault(s => s.Hash == hash);
    if (scan is not null)
    {
        // patch scan.Match in place from the corrected card so the desktop review queue reflects it
    }
    return true;
});
```

## Frontend changes (`OmniCard.Web/Pages/Scan.cshtml`)

All plain JS/CSS — no new client libraries added, keeping the phone page lightweight:

- **Game picker**: a chip/select row above the viewfinder, values from the `CardGame` enum. Selection persists in `localStorage`. Sent as the `game` field on every upload.
- **`uploadImage()`** now awaits a real `ScanResultDto` from `/api/scan` instead of the current `{status, size}` throwaway response.
- **Result screen** (new, replaces the current status-line-only "Sent!" feedback): on `Matched: true`, shows card art (`ImageUri`), name, set/number/rarity, and two actions — **Confirm** (dismiss back to the viewfinder for the next card; no extra network call, the card is already queued on desktop) and **Not this card** (opens the search screen). On `Matched: false` or a populated `Error`, goes straight to the search screen with an explanatory message ("No match found" / "Desktop app not connected — open OmniCard and try again").
- **Search/correction screen** (new): text input debounced against `GET /api/scan/search?game=&q=`, rendered as a tappable list (thumbnail + name/set/number, reusing the result screen's card layout). Tapping a result calls `POST /api/scan/correct`, then shows that card on the result screen as confirmation.

## Error handling

Both `/api/scan` and `/api/scan/correct` return a normal `ScanResultDto`/error payload rather than throwing when the desktop is disconnected or the round trip times out (~15s) — the phone always lands on a clear message screen, never a hung spinner.

## Testing

- Unit tests for `CardService.AddFromStream`'s new `gameOverride` parameter (mirrors existing `AddFromStream`/`FindBestMatch` tests in `OmniCard.Tests`), asserting it matches against the overridden game without mutating `SelectedGame`.
- `ScanController` tests with a mocked `IHubContext`/`DesktopConnectionTracker` covering: desktop connected + match found, desktop connected + no match, desktop not connected, and timeout.
- `Search` endpoint test asserting it passes through to `ICardGameService.SearchCards` unchanged.
- No automated test for the phone-side JS (per CLAUDE.md testing conventions) — the camera/SignalR round trip needs a real device and is manual GUI smoke, same as other recent Web features in this repo.

## Out of scope (per PRD §5)

- No new OCR/recognition model — matching strictly reuses the existing per-game `ICardGameService.FindClosestMatch` pipeline.
- Single-card capture only, no bulk scanning.
- No changes to the core database schema (no auto-commit to inventory; scans still land in the desktop's existing review queue).
