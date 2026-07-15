# Phone Camera Scanning — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow users to snap card photos on their phone and have them appear instantly in the desktop app's scan queue, using the existing matching pipeline.

**Architecture:** Phone browser opens a live camera viewfinder at `/scan` on OmniCard.Web. Captures upload via `POST /api/scan`. The web server broadcasts image bytes to the desktop app via SignalR. The desktop app's `WebScannerService` receives the bytes and calls `CardService.AddFromStream()` — the same pipeline as TWAIN scanning.

**Tech Stack:** ASP.NET Core SignalR, HTML5 getUserMedia API, Microsoft.AspNetCore.SignalR.Client (desktop), Razor Pages

## Global Constraints

- OmniCard.Web targets `net10.0` (ASP.NET Core)
- Desktop app targets `net10.0-windows10.0.22621.0` (WPF)
- SignalR is built into ASP.NET Core — no extra NuGet on the server side
- Desktop needs `Microsoft.AspNetCore.SignalR.Client` NuGet package
- Card matching happens on the desktop, not the web server — the server is a relay
- No authentication in v1 — local network trust model
- `getUserMedia` requires HTTPS or localhost for camera access

---

### Task 1: SignalR Hub + Scan API Endpoint (OmniCard.Web)

Add the server-side infrastructure: a SignalR hub for desktop clients and an API endpoint for phone image uploads.

**Files:**
- Create: `OmniCard.Web/Hubs/ScanHub.cs`
- Create: `OmniCard.Web/Api/ScanController.cs`
- Modify: `OmniCard.Web/Program.cs`

**Interfaces:**
- Produces: `ScanHub` at `/hubs/scan` (SignalR endpoint), `POST /api/scan` accepting multipart form image upload, broadcasting `ImageReceived(byte[] imageData)` to all connected SignalR clients

- [ ] **Step 1: Create the SignalR hub**

```csharp
// OmniCard.Web/Hubs/ScanHub.cs
using Microsoft.AspNetCore.SignalR;

namespace OmniCard.Web.Hubs;

public class ScanHub : Hub
{
    private readonly ILogger<ScanHub> _logger;

    public ScanHub(ILogger<ScanHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Desktop client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Desktop client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
```

- [ ] **Step 2: Create the scan API controller**

```csharp
// OmniCard.Web/Api/ScanController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OmniCard.Web.Hubs;

namespace OmniCard.Web.Api;

[ApiController]
[Route("api/[controller]")]
public class ScanController : ControllerBase
{
    private readonly IHubContext<ScanHub> _hubContext;
    private readonly ILogger<ScanController> _logger;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png"
    };

    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    public ScanController(IHubContext<ScanHub> hubContext, ILogger<ScanController> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> Upload(IFormFile image)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "No image provided" });

        if (!AllowedContentTypes.Contains(image.ContentType))
            return BadRequest(new { error = "Only JPEG and PNG images are accepted" });

        if (image.Length > MaxFileSize)
            return BadRequest(new { error = "Image exceeds 10 MB limit" });

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms);
        var imageData = ms.ToArray();

        _logger.LogInformation("Received scan image: {Size} bytes, {ContentType}", imageData.Length, image.ContentType);

        await _hubContext.Clients.All.SendAsync("ImageReceived", imageData);

        return Ok(new { status = "ok", size = imageData.Length });
    }
}
```

- [ ] **Step 3: Register SignalR and API controllers in Program.cs**

Update `OmniCard.Web/Program.cs`. Add these lines in the appropriate places:

After `builder.Services.AddRazorPages();` add:
```csharp
builder.Services.AddSignalR();
builder.Services.AddControllers();
```

After `app.MapRazorPages();` add:
```csharp
app.MapControllers();
app.MapHub<OmniCard.Web.Hubs.ScanHub>("/hubs/scan");
```

Also add CORS support for the phone camera page (needed for fetch from same origin — already same origin so minimal config):

After `var app = builder.Build();` add:
```csharp
app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());
```

Wait — SignalR doesn't work with `AllowAnyOrigin` when using credentials. Since the phone page is served from the same origin, CORS isn't needed for the phone→API call. And the desktop SignalR client uses WebSockets which don't enforce CORS. Remove the CORS block. No CORS configuration needed.

- [ ] **Step 4: Build and verify**

```bash
cd d:/source/repos/OmniCard
dotnet build OmniCard.Web/OmniCard.Web.csproj
```

Expected: BUILD SUCCEEDED

- [ ] **Step 5: Commit**

```bash
git add OmniCard.Web/Hubs/ScanHub.cs OmniCard.Web/Api/ScanController.cs OmniCard.Web/Program.cs
git commit -m "feat: add SignalR hub and scan API endpoint for phone camera scanning"
```

---

### Task 2: Phone Camera Viewfinder Page

Create the mobile-optimized camera page at `/scan` with live viewfinder, capture button, and auto-upload.

**Files:**
- Create: `OmniCard.Web/Pages/Scan.cshtml`

**Interfaces:**
- Consumes: `POST /api/scan` (from Task 1) — uploads JPEG image as multipart form data
- Produces: Mobile camera page at `/scan` URL

- [ ] **Step 1: Create the camera page**

```html
@page
@{
    ViewData["Title"] = "Phone Scanner";
}
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8"/>
    <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no"/>
    <title>OmniCard Scanner</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        html, body { height: 100%; overflow: hidden; background: #000; color: #fff; font-family: -apple-system, BlinkMacSystemFont, sans-serif; }

        .container { display: flex; flex-direction: column; height: 100%; position: relative; }

        /* Viewfinder */
        .viewfinder { flex: 1; position: relative; overflow: hidden; display: flex; align-items: center; justify-content: center; }
        .viewfinder video { width: 100%; height: 100%; object-fit: cover; }
        .viewfinder canvas { display: none; }

        /* Card outline guide */
        .card-guide {
            position: absolute; top: 50%; left: 50%;
            transform: translate(-50%, -50%);
            width: 65%; aspect-ratio: 63/88; /* Standard card ratio */
            border: 2px solid rgba(255,255,255,0.5);
            border-radius: 12px;
            pointer-events: none;
        }

        /* Flash overlay */
        .flash {
            position: absolute; top: 0; left: 0; right: 0; bottom: 0;
            background: #fff; opacity: 0; pointer-events: none;
            transition: opacity 0.1s;
        }
        .flash.active { opacity: 0.7; transition: none; }

        /* Controls */
        .controls {
            padding: 20px; display: flex; align-items: center; justify-content: center;
            background: rgba(0,0,0,0.8);
        }
        .capture-btn {
            width: 72px; height: 72px; border-radius: 50%;
            border: 4px solid #fff; background: transparent;
            cursor: pointer; position: relative;
            -webkit-tap-highlight-color: transparent;
        }
        .capture-btn::after {
            content: ''; position: absolute;
            top: 4px; left: 4px; right: 4px; bottom: 4px;
            border-radius: 50%; background: #fff;
        }
        .capture-btn:active::after { background: #ccc; }
        .capture-btn:disabled { opacity: 0.4; }

        /* Status bar */
        .status {
            padding: 10px 16px; background: rgba(0,0,0,0.9);
            font-size: 14px; text-align: center; min-height: 44px;
            display: flex; align-items: center; justify-content: center;
        }
        .status.success { color: #4caf50; }
        .status.error { color: #f44336; }
        .status.uploading { color: #ffeb3b; }

        /* Error state */
        .error-screen {
            flex: 1; display: flex; align-items: center; justify-content: center;
            padding: 24px; text-align: center;
        }
        .error-screen p { margin-top: 12px; color: #aaa; font-size: 14px; }
    </style>
</head>
<body>
    <div class="container" id="app">
        <div class="status" id="status">Initializing camera...</div>
        <div class="viewfinder" id="viewfinder">
            <video id="video" autoplay playsinline muted></video>
            <canvas id="canvas"></canvas>
            <div class="card-guide"></div>
            <div class="flash" id="flash"></div>
        </div>
        <div class="controls">
            <button class="capture-btn" id="captureBtn" disabled></button>
        </div>
    </div>

    <div class="container" id="errorScreen" style="display:none">
        <div class="error-screen">
            <h2>Camera Not Available</h2>
            <p id="errorMessage"></p>
        </div>
    </div>

    <script>
        const video = document.getElementById('video');
        const canvas = document.getElementById('canvas');
        const captureBtn = document.getElementById('captureBtn');
        const status = document.getElementById('status');
        const flash = document.getElementById('flash');
        let isUploading = false;

        async function initCamera() {
            try {
                const stream = await navigator.mediaDevices.getUserMedia({
                    video: {
                        facingMode: 'environment',
                        width: { ideal: 1920 },
                        height: { ideal: 1080 }
                    },
                    audio: false
                });
                video.srcObject = stream;
                await video.play();
                captureBtn.disabled = false;
                setStatus('Ready — tap to scan', '');
            } catch (err) {
                showError(err.message || 'Could not access camera');
            }
        }

        function setStatus(text, className) {
            status.textContent = text;
            status.className = 'status ' + (className || '');
        }

        function showError(msg) {
            document.getElementById('app').style.display = 'none';
            document.getElementById('errorScreen').style.display = '';
            document.getElementById('errorMessage').textContent = msg;
        }

        function flashEffect() {
            flash.classList.add('active');
            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    flash.classList.remove('active');
                });
            });
        }

        async function capture() {
            if (isUploading) return;
            isUploading = true;
            captureBtn.disabled = true;

            flashEffect();

            // Draw video frame to canvas
            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;
            const ctx = canvas.getContext('2d');
            ctx.drawImage(video, 0, 0);

            setStatus('Uploading...', 'uploading');

            try {
                const blob = await new Promise(resolve =>
                    canvas.toBlob(resolve, 'image/jpeg', 0.9)
                );

                const formData = new FormData();
                formData.append('image', blob, 'scan.jpg');

                const response = await fetch('/api/scan', {
                    method: 'POST',
                    body: formData
                });

                if (response.ok) {
                    const data = await response.json();
                    setStatus('Sent! Ready for next card.', 'success');
                } else {
                    const err = await response.json().catch(() => ({}));
                    setStatus(err.error || 'Upload failed', 'error');
                }
            } catch (err) {
                setStatus('Network error — is the server running?', 'error');
            }

            isUploading = false;
            captureBtn.disabled = false;
        }

        captureBtn.addEventListener('click', capture);

        // Prevent zoom on double-tap
        document.addEventListener('dblclick', e => e.preventDefault());

        initCamera();
    </script>
</body>
</html>
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build OmniCard.Web/OmniCard.Web.csproj
```

Expected: BUILD SUCCEEDED

- [ ] **Step 3: Manual test**

Start the web server:
```bash
dotnet run --project OmniCard.Web/OmniCard.Web.csproj --db "D:\path\to\your\data"
```

Open `http://localhost:5000/scan` in a desktop browser. The page should load, show "Camera Not Available" (desktop browsers often block getUserMedia without HTTPS), but the page renders and the JavaScript runs without errors.

On the same machine, test the API endpoint directly:
```bash
curl -X POST http://localhost:5000/api/scan -F "image=@path/to/test.jpg"
```

Expected: `{"status":"ok","size":12345}`

- [ ] **Step 4: Commit**

```bash
git add OmniCard.Web/Pages/Scan.cshtml
git commit -m "feat: add phone camera viewfinder page at /scan"
```

---

### Task 3: Desktop WebScannerService (SignalR Client)

Create the desktop-side service that connects to the OmniCard.Web SignalR hub and feeds received images into the existing scan pipeline.

**Files:**
- Create: `OmniCard/Services/WebScannerService.cs`
- Modify: `OmniCard/OmniCard.csproj` (add SignalR client NuGet)
- Modify: `OmniCard/App.xaml.cs` (register and start WebScannerService)

**Interfaces:**
- Consumes: `ScanHub` at `/hubs/scan` (from Task 1) — receives `ImageReceived(byte[] imageData)` events
- Consumes: `ICardService.AddFromStream(Stream)` — existing scan pipeline entry point
- Produces: `WebScannerService` singleton — automatically connects to SignalR hub and feeds phone camera images into the scan queue

- [ ] **Step 1: Add SignalR client NuGet package**

Add to `OmniCard/OmniCard.csproj` in an `<ItemGroup>`:

```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.9" />
```

- [ ] **Step 2: Create WebScannerService**

```csharp
// OmniCard/Services/WebScannerService.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Services;

public sealed class WebScannerService : IAsyncDisposable
{
    private readonly ICardService _cardService;
    private readonly ILogger<WebScannerService> _logger;
    private readonly IOptionsMonitor<WebCompanionSettings> _settings;
    private HubConnection? _hubConnection;
    private IDisposable? _settingsChangeToken;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public WebScannerService(
        ICardService cardService,
        ILogger<WebScannerService> logger,
        IOptionsMonitor<WebCompanionSettings> settings)
    {
        _cardService = cardService;
        _logger = logger;
        _settings = settings;
    }

    public async Task StartAsync()
    {
        var baseUrl = _settings.CurrentValue.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogInformation("WebCompanion BaseUrl not configured — phone scanner disabled");
            return;
        }

        await ConnectAsync(baseUrl);

        // Reconnect if settings change
        _settingsChangeToken = _settings.OnChange(async newSettings =>
        {
            var newUrl = newSettings.BaseUrl;
            if (string.IsNullOrWhiteSpace(newUrl))
            {
                await DisconnectAsync();
                return;
            }

            // Reconnect if URL changed
            if (_hubConnection is null || _hubConnection.State == HubConnectionState.Disconnected)
                await ConnectAsync(newUrl);
        });
    }

    private async Task ConnectAsync(string baseUrl)
    {
        await DisconnectAsync();

        var hubUrl = $"{baseUrl.TrimEnd('/')}/hubs/scan";
        _logger.LogInformation("Connecting to phone scanner hub at {Url}", hubUrl);

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<byte[]>("ImageReceived", OnImageReceived);

        _hubConnection.Reconnecting += _ =>
        {
            _logger.LogWarning("Phone scanner connection lost, reconnecting...");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += _ =>
        {
            _logger.LogInformation("Phone scanner reconnected");
            return Task.CompletedTask;
        };

        try
        {
            await _hubConnection.StartAsync();
            _logger.LogInformation("Connected to phone scanner hub");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to phone scanner hub at {Url} — phone scanning unavailable", hubUrl);
        }
    }

    private void OnImageReceived(byte[] imageData)
    {
        _logger.LogInformation("Received image from phone: {Size} bytes", imageData.Length);
        try
        {
            using var stream = new MemoryStream(imageData);
            _cardService.AddFromStream(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process phone scan image");
        }
    }

    private async Task DisconnectAsync()
    {
        if (_hubConnection is not null)
        {
            try
            {
                await _hubConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disconnecting from phone scanner hub");
            }
            _hubConnection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _settingsChangeToken?.Dispose();
        await DisconnectAsync();
    }
}
```

- [ ] **Step 3: Register WebScannerService in App.xaml.cs**

In `OmniCard/App.xaml.cs`, add the service registration in the `ConfigureServices` block (after the scanner service registration around line 74):

```csharp
services.AddSingleton<WebScannerService>();
```

Add `using OmniCard.Services;` if not already present (it should be, for `DialogService`).

- [ ] **Step 4: Start WebScannerService on app startup**

In `OmniCard/App.xaml.cs`, in the `OnStartup` method, after `Host.Start();` (around line 274), add:

```csharp
// Start phone scanner connection (non-blocking)
var webScanner = Host.Services.GetRequiredService<WebScannerService>();
_ = webScanner.StartAsync();
```

- [ ] **Step 5: Build the entire solution**

```bash
dotnet build OmniCard.slnx
```

Expected: BUILD SUCCEEDED

- [ ] **Step 6: Run tests**

```bash
dotnet test OmniCard.Tests/OmniCard.Tests.csproj --verbosity quiet
```

Expected: All 326 tests pass (WebScannerService has no tests — it's infrastructure glue)

- [ ] **Step 7: Commit**

```bash
git add OmniCard/Services/WebScannerService.cs OmniCard/OmniCard.csproj OmniCard/App.xaml.cs
git commit -m "feat: add WebScannerService — desktop SignalR client for phone camera scanning"
```

---

### Task 4: End-to-End Verification

Verify the complete phone → web → desktop pipeline works together.

**Files:**
- No files created or modified — this is a manual verification task

**Interfaces:**
- Consumes: All outputs from Tasks 1-3

- [ ] **Step 1: Configure WebCompanion BaseUrl**

In `OmniCard/appsettings.json`, set the WebCompanion URL to point to the local web server:
```json
"WebCompanion": {
    "BaseUrl": "http://localhost:5000"
}
```

(For phone testing, use the machine's LAN IP instead of localhost)

- [ ] **Step 2: Start OmniCard.Web**

```bash
dotnet run --project OmniCard.Web/OmniCard.Web.csproj --db "D:\path\to\data"
```

Verify in console output: SignalR hub is mapped and server is listening.

- [ ] **Step 3: Start desktop app**

Launch OmniCard. Check logs for:
```
Connected to phone scanner hub
```

If BaseUrl is empty, expect:
```
WebCompanion BaseUrl not configured — phone scanner disabled
```

- [ ] **Step 4: Test with curl (simulate phone upload)**

```bash
curl -X POST http://localhost:5000/api/scan -F "image=@test-card.jpg"
```

Expected:
1. Web server logs: `Received scan image: XXXXX bytes`
2. Desktop app logs: `Received image from phone: XXXXX bytes`
3. A scanned card appears in the scan queue (same as TWAIN scan)

- [ ] **Step 5: Test with phone browser**

Open `http://<LAN-IP>:5000/scan` on your phone. Verify:
1. Camera viewfinder loads (rear camera)
2. Card guide overlay visible
3. Tap capture → flash → "Sent! Ready for next card."
4. Card appears in desktop scan queue within 1-2 seconds
5. Card matches correctly (name, set, confidence)
6. Commit the scan — saves to collection like any TWAIN scan

- [ ] **Step 6: Commit appsettings change (if needed)**

If you updated appsettings.json to a non-empty BaseUrl for testing, decide whether to keep it or revert. The default should remain empty (user configures their own URL).

```bash
git add -A
git commit -m "feat: complete phone camera scanning — end-to-end verified"
```
