# Out-of-Process Scanner Host

**Date:** 2026-07-12
**Status:** Approved

## Overview

Move the TWAIN scan operation into a separate console process (`OmniCard.ScannerHost.exe`) so that native driver crashes (like the Epson ET-8500's `STATUS_STACK_BUFFER_OVERRUN`) kill only the host process, not the main application. The main app launches the host, waits for it to finish, and reads the scanned image from a temp file.

## Background

The Epson ET-8500's TWAIN driver crashes with `0xc0000409` during scan transfer in all modes (NoUI, ShowUI, Native, File). This is a native crash that bypasses all .NET exception handling and kills the entire process. Running the scan in a separate process isolates the crash.

## Scanner Host Process

### New Project: `OmniCard.ScannerHost`

A .NET console application that:

1. Parses command-line arguments
2. Opens a TWAIN session
3. Connects to the named scanner data source
4. Applies scan settings (resolution, pixel type, duplex off, foil adjustments)
5. Executes the scan
6. Writes the scanned image to the specified output file path
7. Exits with a status code

### Command-Line Interface

```
OmniCard.ScannerHost.exe --scanner "EPSON ET-8500 Series" --output "C:\temp\scan.bmp" --dpi 200 --show-ui
```

**Arguments:**
- `--scanner` (required) -- TWAIN data source name to connect to
- `--output` (required) -- file path to write the scanned image
- `--dpi` (optional, default 200) -- scan resolution
- `--show-ui` (flag) -- use `SourceEnableMode.ShowUI` instead of `NoUI`
- `--foil` (flag) -- apply foil card brightness/contrast settings (brightness -200, auto-bright off, contrast 333.33)

### Exit Codes

- `0` -- success, image written to `--output` path
- `1` -- scanner not found (data source name didn't match any TWAIN source)
- `2` -- scan failed or driver error (managed exception caught)
- `3` -- no image data transferred (scan completed but no image received)

Error details are written to stderr.

### Scan Settings

The host applies the same settings the main app currently applies:
- Pixel type: RGB
- ICC profile: Embed
- Duplex: disabled
- Resolution: from `--dpi` arg
- Foil adjustments: only when `--foil` flag is present

### Message Pump

TWAIN requires a Win32 message pump to deliver scan events. The host must run a message loop (e.g., `Application.Run()` from `System.Windows.Forms` or a manual `GetMessage`/`DispatchMessage` loop) and exit the loop when the scan completes or fails. The NTwain `TwainSession` uses the message pump to fire `TransferReady`, `DataTransferred`, and `SourceDisabled` events.

### Dependencies

- References `NTwain` package (same version as `OmniCard.Scanner`)
- References `System.Windows.Forms` for the message pump (or uses manual P/Invoke -- WinForms is simpler)
- No reference to `OmniCard.Scanner` -- the host contains its own self-contained TWAIN logic (small duplication is acceptable to keep the host minimal and dependency-free)

## Main App Integration

### ScannerService Changes

- `Scan(bool showUI)` becomes `async Task ScanAsync(bool showUI)`
- Instead of calling `DataSource.Enable` directly, it:
  1. Builds the command-line args from current settings (scanner name, DPI, showUI, foil)
  2. Generates a temp output file path
  3. Launches `OmniCard.ScannerHost.exe` as a `Process`
  4. Awaits `process.WaitForExitAsync()`
  5. On exit code 0: opens the output file and calls `CardService.AddFromStream`
  6. On non-zero exit code: sets `LastScanError` with a message suggesting the user try "Import from Folder"
  7. On process crash (abnormal exit): same error handling -- main app stays alive
- The TWAIN session remains in the main app for **scanner discovery only** (listing data sources in the connection dialog). Discovery doesn't trigger the crash -- only scan transfer does.
- The `WindowHandle` property is no longer needed for scanning (the host creates its own window if ShowUI is used)

### RootViewModel Changes

- `Scan()` command becomes async, awaits `ScannerService.ScanAsync(ShowScannerUI)`
- Error display remains the same (reads `LastScanError`)

### Deployment

- `OmniCard.csproj` adds a project reference to `OmniCard.ScannerHost` so it builds and copies to the output directory alongside `OmniCard.exe`
- The scanner service resolves the host exe path via `Path.Combine(AppContext.BaseDirectory, "OmniCard.ScannerHost.exe")`

## Files

### New Files

| File | Purpose |
|---|---|
| `OmniCard.ScannerHost/OmniCard.ScannerHost.csproj` | Console exe project, references NTwain |
| `OmniCard.ScannerHost/Program.cs` | Arg parsing, TWAIN session, scan, write image, exit |

### Modified Files

| File | Change |
|---|---|
| `OmniCard.Scanner/ScannerService.cs` | Replace `DataSource.Enable` with `Process.Start` of host exe. `Scan` becomes `ScanAsync`. Keep TWAIN session for discovery. |
| `OmniCard/Views/Root/RootViewModel.cs` | Await `ScannerService.ScanAsync()` |
| `OmniCard/OmniCard.csproj` | Add project reference to ScannerHost |

### Unchanged

- Connection dialog (scanner discovery via TWAIN in-process -- safe)
- Import from Folder feature
- Phone scanner (WebScannerService)
- `CardService.AddFromStream` pipeline

## Dependencies

No new NuGet packages. The host project uses `NTwain` (already in the solution) and standard .NET libraries.
