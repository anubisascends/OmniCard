# Scanner UI Toggle and Folder Import

**Date:** 2026-07-12
**Status:** Approved

## Overview

Two scanner resilience features: (1) a "Show Scanner UI" toggle that lets TWAIN drivers use their own UI (fixing crashes with network scanners like the Epson ET-8500), and (2) an "Import from Folder" button that ingests pre-scanned card images through the same pipeline as hardware scans.

## Background

The Epson ET-8500 (network-attached) crashes with `STATUS_STACK_BUFFER_OVERRUN` (`0xc0000409`) when scanning in `NoUI` mode. This is a native driver bug that cannot be caught by .NET exception handling. The ShowUI toggle lets the driver use its own tested UI. The folder import provides a guaranteed fallback for any scanner that misbehaves.

## Show Scanner UI Toggle

### Settings

- Add `ShowScannerUI` bool to `DisplaySettings` (default `false`, persisted to appsettings.json).

### UI

- Add a checkbox in the scanner toolbar next to the quality selector: `[ ] Show Scanner UI`.

### Behavior

- When enabled: `DataSource.Enable(SourceEnableMode.ShowUI, true, WindowHandle)` -- the scanner driver shows its own scan dialog.
- When disabled: `DataSource.Enable(SourceEnableMode.NoUI, false, WindowHandle)` -- current headless behavior.
- `ScannerService.Scan()` accepts a `bool showUI` parameter.
- Wrap the `DataSource.Enable` call in a try/catch for managed exceptions. On failure, log a warning and show a message suggesting the user enable Show Scanner UI or use Import from Folder.

## Folder Import

### UI

- Add an "Import from Folder..." button in the scanner toolbar (next to the ShowUI checkbox).

### Behavior

1. Clicking the button opens a `FolderBrowserDialog` to select a folder.
2. The app scans the folder for image files: `.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff`, `.tif`.
3. For each image found:
   - Copy the file to the temp scans directory (same location TWAIN scans go).
   - Verify the copy succeeded (file exists, size matches source).
   - Delete the original file from the source folder.
   - Open the copied file as a stream and pass it through `CardService.AddFromStream` -- same pipeline as a hardware scan (auto-crop, hashing, matching, OCR).
4. Status message updates during import: "Importing 3/10..."
5. Non-image files in the folder are ignored silently.
6. Empty folders (no image files) show a message: "No image files found in the selected folder."

## Files Modified

| File | Change |
|---|---|
| `OmniCard.Shared/Models/DisplaySettings.cs` | Add `ShowScannerUI` bool (default false) |
| `OmniCard.Scanner/ScannerService.cs` | Accept `showUI` param in `Scan()`, use ShowUI/NoUI accordingly, try/catch around Enable |
| `OmniCard/Views/Root/RootViewModel.cs` | Add `ShowScannerUI` property with persistence, pass to `Scan()`, add `ImportFromFolderCommand` |
| `OmniCard/Views/Root/ScannerTabView.xaml` | Add ShowUI checkbox and Import from Folder button in toolbar |

## Dependencies

No new NuGet packages. Uses `Microsoft.Win32.OpenFolderDialog` (.NET 8+), `CardService.AddFromStream`, and `ScanImageCache.TempScansDirectory`.
