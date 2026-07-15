# Phone Camera Scanning — Design Spec

## Context

OmniCard currently supports card scanning only via TWAIN flatbed scanners. The goal is to allow users to use their phone camera as a wireless scanner — open a URL on the phone browser, snap a card photo, and have it appear in the desktop app's scan queue instantly, going through the same matching pipeline (pHash, art hashes, OCR, auto-crop).

## Architecture Overview

```
Phone Browser                    OmniCard.Web (ASP.NET)              Desktop App (WPF)
─────────────                    ──────────────────────              ─────────────────
Live viewfinder  ──HTTP POST──>  POST /api/scan         ──SignalR──>  WebScannerService
(getUserMedia)                   saves temp image                     receives byte[]
Tap to capture                   broadcasts via SignalR               wraps in MemoryStream
                                                                      calls CardService
Shows confirmation <──response── returns { status: ok }               .AddFromStream()
Ready for next scan                                                   Card appears in queue
```

**Key principle:** The web server is a relay, not a processor. Card matching happens on the desktop app using the existing pipeline. The web server receives the phone image and pushes it to the desktop via SignalR.

## Component 1: OmniCard.Web — Scan API + SignalR Hub

### SignalR Hub

```csharp
// OmniCard.Web/Hubs/ScanHub.cs
public class ScanHub : Hub
{
    // Desktop clients connect here to receive images
    // No methods needed on the hub itself — server pushes to clients
}
```

Register in `Program.cs`:
```csharp
builder.Services.AddSignalR();
app.MapHub<ScanHub>("/hubs/scan");
```

### Scan API Endpoint

```csharp
// OmniCard.Web/Api/ScanController.cs
[ApiController]
[Route("api/[controller]")]
public class ScanController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile image)
    {
        // 1. Validate: must be JPEG/PNG, max 10MB
        // 2. Read into byte[]
        // 3. Broadcast to desktop clients:
        //    await hubContext.Clients.All.SendAsync("ImageReceived", imageBytes);
        // 4. Return 200 OK
    }
}
```

### Dependencies

OmniCard.Web already references OmniCard.Shared and OmniCard.Data. SignalR is built into ASP.NET Core — no additional NuGet packages needed.

## Component 2: Phone Camera Page (`/scan`)

A single Razor page served by OmniCard.Web at `/scan`.

### Camera Access

```javascript
// Request rear camera
const stream = await navigator.mediaDevices.getUserMedia({
    video: { facingMode: 'environment', width: { ideal: 1920 }, height: { ideal: 1080 } }
});
videoElement.srcObject = stream;
```

### Capture Flow

1. User taps capture button
2. Draw current video frame to an off-screen `<canvas>`
3. Convert canvas to JPEG blob: `canvas.toBlob(callback, 'image/jpeg', 0.9)`
4. Upload via `fetch('/api/scan', { method: 'POST', body: formData })`
5. Show brief visual confirmation (flash effect)
6. Camera stays active — ready for next card immediately

### UI Layout (Mobile-Optimized)

- Full-screen camera viewfinder (no chrome)
- Large circular capture button at bottom center
- Small status text showing last result ("Matched: Lightning Bolt - Alpha")
- Settings gear for toggling flash (if device supports `torch` constraint)
- Landscape and portrait support

### Browser Compatibility

- `getUserMedia` requires HTTPS or localhost. Since OmniCard.Web runs on the local network, users will access via `https://192.168.x.x:5001` or `http://192.168.x.x:5000` (localhost is exempt from HTTPS requirement for the phone if accessing via IP — but only on some browsers. May need to use HTTPS with a self-signed cert or accept the browser warning).
- Works on iOS Safari 11+, Android Chrome, Firefox Mobile.

## Component 3: Desktop App — WebScannerService

A new service that connects to the OmniCard.Web SignalR hub and feeds images into the existing scan pipeline.

### Service Design

```csharp
// Could live in OmniCard.Scanner or the main OmniCard project
public class WebScannerService : IDisposable
{
    private readonly ICardService _cardService;
    private readonly HubConnection _hubConnection;

    public WebScannerService(ICardService cardService, IOptions<WebCompanionSettings> settings)
    {
        _cardService = cardService;
        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{settings.Value.BaseUrl}/hubs/scan")
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<byte[]>("ImageReceived", OnImageReceived);
    }

    private void OnImageReceived(byte[] imageData)
    {
        using var stream = new MemoryStream(imageData);
        _cardService.AddFromStream(stream);
        // Card appears in ScannedCards collection — same as TWAIN
    }

    public async Task ConnectAsync() => await _hubConnection.StartAsync();
    public async Task DisconnectAsync() => await _hubConnection.StopAsync();
}
```

### NuGet Dependency

The desktop app needs `Microsoft.AspNetCore.SignalR.Client` to connect to the hub.

### Integration with Existing UI

No changes to the scanner tab UI. Cards from the phone appear in the same `ScannedCards` collection as TWAIN cards. The user reviews, corrects, and commits them the same way.

The only UI addition: a connection status indicator showing whether the phone scanner is connected (e.g., a small icon or text in the scanner tab header).

### Lifecycle

- `WebScannerService` starts when the web companion URL is configured (`WebCompanionSettings.BaseUrl` is non-empty)
- Connects on app startup, auto-reconnects on disconnect
- Stops on app shutdown

## What Stays the Same

- The entire matching pipeline: auto-crop, pHash, art hashes, OCR, confidence scoring, flagging
- Scanner tab UI: scanned cards display identically regardless of source
- Commit flow: same `CommitScans()` for phone-scanned and flatbed-scanned cards
- TWAIN scanning: phone is additive, not a replacement

## Scope & Sequencing

This feature touches 3 projects and has natural phases:

1. **Phase 1: SignalR hub + API endpoint** (OmniCard.Web) — server-side relay
2. **Phase 2: Phone camera page** (OmniCard.Web/Pages/Scan) — mobile viewfinder UI
3. **Phase 3: Desktop SignalR client** (OmniCard/WebScannerService) — receives and processes images

Each phase is independently testable. Phase 1+2 can be tested by uploading images and checking the SignalR broadcast. Phase 3 connects everything end-to-end.

## Network & Security Considerations

- Phone and desktop must be on the same local network (or the web server must be accessible from the phone)
- No authentication is added in v1 — the web server is local/trusted. Future enhancement: optional PIN/pairing code
- Image data is transmitted as raw bytes over SignalR (binary protocol). For large images, consider chunking or limiting resolution on the phone side before upload
- HTTPS: `getUserMedia` requires a secure context. Options:
  - Use `http://` on localhost (exempted by browsers)
  - Generate a self-signed cert for the local IP
  - Use a tunnel like `ngrok` (overkill for local use)
  - Most pragmatic: accept the browser security warning on the phone for the local IP

## Files to Create/Modify

| File | Action |
|------|--------|
| `OmniCard.Web/Hubs/ScanHub.cs` | Create — SignalR hub |
| `OmniCard.Web/Api/ScanController.cs` | Create — image upload endpoint |
| `OmniCard.Web/Pages/Scan.cshtml` | Create — phone camera viewfinder page |
| `OmniCard.Web/Program.cs` | Modify — register SignalR, add API controllers |
| `OmniCard/Services/WebScannerService.cs` | Create — SignalR client in desktop app |
| `OmniCard/App.xaml.cs` | Modify — register and start WebScannerService |
| `OmniCard/OmniCard.csproj` | Modify — add Microsoft.AspNetCore.SignalR.Client |
| `OmniCard.Web/OmniCard.Web.csproj` | Modify — add project refs if needed |

## Verification

1. Start OmniCard.Web on local network
2. Open `/scan` on phone browser — camera viewfinder should appear
3. Start OmniCard desktop app (with WebCompanion BaseUrl configured)
4. Snap a card photo on phone — card should appear in desktop scan queue within 1-2 seconds
5. Verify matching works (name, set, confidence displayed correctly)
6. Commit the scan — verify it saves to collection like any TWAIN scan
