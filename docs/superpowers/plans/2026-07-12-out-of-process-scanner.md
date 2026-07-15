# Out-of-Process Scanner Host Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the TWAIN scan operation into a separate console process so that native driver crashes don't kill the main application.

**Architecture:** A new `OmniCard.ScannerHost` console exe handles the TWAIN scan and writes the image to a temp file. The main app's `ScannerService` launches this process, waits for it, and reads the result. Scanner discovery stays in-process (safe). If the host crashes, the main app catches the abnormal exit and shows a message.

**Tech Stack:** C#/.NET 10, NTwain, System.Windows.Forms (message pump), System.Diagnostics.Process.

## Global Constraints

- No new NuGet packages beyond what's already in the solution (NTwain is already used).
- The host exe must be a Windows application (TWAIN requires a message pump).
- Exit codes: 0 = success, 1 = scanner not found, 2 = scan failed, 3 = no image transferred.
- The main app's `ScannerService` keeps the TWAIN session for scanner discovery only.
- The host duplicates scan settings logic (small, acceptable duplication to keep the host dependency-free).

---

### Task 1: Create ScannerHost Console Application

**Files:**
- Create: `OmniCard.ScannerHost/OmniCard.ScannerHost.csproj`
- Create: `OmniCard.ScannerHost/Program.cs`

**Interfaces:**
- Consumes: NTwain API (same as `OmniCard.Scanner`)
- Produces: An executable `OmniCard.ScannerHost.exe` that accepts `--scanner`, `--output`, `--dpi`, `--show-ui`, `--foil` args, performs a TWAIN scan, writes the image to `--output`, and exits with a status code.

- [ ] **Step 1: Create the project file**

Create `OmniCard.ScannerHost/OmniCard.ScannerHost.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NTwain" Version="3.7.6" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Implement Program.cs**

Create `OmniCard.ScannerHost/Program.cs`:

```csharp
using System.Reflection;
using NTwain;
using NTwain.Data;

namespace OmniCard.ScannerHost;

static class Program
{
    private static string? _outputPath;
    private static bool _imageReceived;
    private static int _exitCode = 3; // default: no image

    [STAThread]
    static int Main(string[] args)
    {
        string? scannerName = null;
        int dpi = 200;
        bool showUI = false;
        bool foil = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scanner" when i + 1 < args.Length:
                    scannerName = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    _outputPath = args[++i];
                    break;
                case "--dpi" when i + 1 < args.Length:
                    int.TryParse(args[++i], out dpi);
                    break;
                case "--show-ui":
                    showUI = true;
                    break;
                case "--foil":
                    foil = true;
                    break;
            }
        }

        if (scannerName is null || _outputPath is null)
        {
            Console.Error.WriteLine("Usage: OmniCard.ScannerHost --scanner <name> --output <path> [--dpi N] [--show-ui] [--foil]");
            return 2;
        }

        try
        {
            var appId = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
            var session = new TwainSession(appId);

            session.TransferReady += (_, _) => { };
            session.DataTransferred += OnDataTransferred;
            session.TransferError += (_, e) =>
            {
                Console.Error.WriteLine($"Transfer error: {e.ReturnCode}");
                _exitCode = 2;
                Application.ExitThread();
            };
            session.SourceDisabled += (_, _) =>
            {
                Application.ExitThread();
            };

            session.Open();

            var source = session.OfType<DataSource>()
                .FirstOrDefault(s => string.Equals(s.Name, scannerName, StringComparison.OrdinalIgnoreCase));

            if (source is null)
            {
                Console.Error.WriteLine($"Scanner not found: {scannerName}");
                session.Close();
                return 1;
            }

            source.Open();
            ApplySettings(source, dpi, foil);

            var mode = showUI ? SourceEnableMode.ShowUI : SourceEnableMode.NoUI;
            source.Enable(mode, showUI, IntPtr.Zero);

            // Run message loop until scan completes or fails
            Application.Run();

            source.Close();
            session.Close();

            return _exitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Scanner error: {ex.Message}");
            return 2;
        }
    }

    private static void OnDataTransferred(object? sender, DataTransferredEventArgs e)
    {
        try
        {
            using var stream = e.GetNativeImageStream();
            if (stream is null)
            {
                Console.Error.WriteLine("No image data in transfer");
                _exitCode = 3;
                return;
            }

            var dir = Path.GetDirectoryName(_outputPath);
            if (dir is not null)
                Directory.CreateDirectory(dir);

            using var file = File.Create(_outputPath!);
            stream.CopyTo(file);
            _imageReceived = true;
            _exitCode = 0;
            Console.Out.WriteLine($"Image written to {_outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save image: {ex.Message}");
            _exitCode = 2;
        }
    }

    private static void ApplySettings(DataSource ds, int dpi, bool foil)
    {
        var caps = ds.Capabilities;

        // Pixel type: RGB
        try { if (caps.ICapPixelType.CanSet) caps.ICapPixelType.SetValue(PixelType.RGB); }
        catch { }

        // ICC profile: Embed
        try { if (caps.ICapICCProfile.CanSet) caps.ICapICCProfile.SetValue(IccProfile.Embed); }
        catch { }

        // Duplex: off
        try { if (caps.CapDuplexEnabled.CanSet) caps.CapDuplexEnabled.SetValue(BoolType.False); }
        catch { }

        // Resolution
        try { if (caps.ICapXResolution.CanSet) caps.ICapXResolution.SetValue((TWFix32)(float)dpi); }
        catch { }
        try { if (caps.ICapYResolution.CanSet) caps.ICapYResolution.SetValue((TWFix32)(float)dpi); }
        catch { }

        // Reset image processing
        try { if (caps.ICapAutoBright.CanReset) caps.ICapAutoBright.Reset(); } catch { }
        try { if (caps.ICapBrightness.CanReset) caps.ICapBrightness.Reset(); } catch { }
        try { if (caps.ICapContrast.CanReset) caps.ICapContrast.Reset(); } catch { }
        try { if (caps.ICapGamma.CanReset) caps.ICapGamma.Reset(); } catch { }
        try { if (caps.ICapHighlight.CanReset) caps.ICapHighlight.Reset(); } catch { }
        try { if (caps.ICapShadow.CanReset) caps.ICapShadow.Reset(); } catch { }

        // Foil adjustments
        if (foil)
        {
            try { if (caps.ICapAutoBright.CanSet) caps.ICapAutoBright.SetValue(BoolType.False); } catch { }
            try { if (caps.ICapBrightness.CanSet) caps.ICapBrightness.SetValue((TWFix32)(-200f)); } catch { }
            try { if (caps.ICapContrast.CanSet) caps.ICapContrast.SetValue((TWFix32)333.3333f); } catch { }
        }
    }
}
```

- [ ] **Step 3: Build the host project**

Run: `dotnet build OmniCard.ScannerHost/OmniCard.ScannerHost.csproj`
Expected: Build succeeds, produces `OmniCard.ScannerHost.exe`.

- [ ] **Step 4: Commit**

```bash
git add OmniCard.ScannerHost/
git commit -m "feat(scanner): add out-of-process scanner host console application"
```

---

### Task 2: Rewire ScannerService to Launch Host Process

**Files:**
- Modify: `OmniCard.Scanner/ScannerService.cs`
- Modify: `OmniCard/Views/Root/RootViewModel.cs`
- Modify: `OmniCard/OmniCard.csproj`

**Interfaces:**
- Consumes: `OmniCard.ScannerHost.exe` (Task 1) -- launched as a `Process` with command-line args
- Produces: `ScannerService.ScanAsync(bool showUI)` replaces `Scan(bool showUI)`. Returns `Task`. Sets `LastScanError` on failure. Calls `CardService.AddFromStream` on success.

- [ ] **Step 1: Add ScannerHost as a build dependency**

Add to `OmniCard/OmniCard.csproj` inside the existing `<ItemGroup>` with ProjectReferences:

```xml
    <ProjectReference Include="..\OmniCard.ScannerHost\OmniCard.ScannerHost.csproj">
      <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
    </ProjectReference>
```

Note: `ReferenceOutputAssembly=false` means we don't reference its types -- we just need it built. We also need to copy the exe to our output. Add this new ItemGroup after the ProjectReferences:

```xml
  <Target Name="CopyScannerHost" AfterTargets="Build">
    <Copy SourceFiles="$(OutputPath)..\OmniCard.ScannerHost\$(TargetFramework)\OmniCard.ScannerHost.exe"
          DestinationFolder="$(OutputPath)"
          SkipUnchangedFiles="true"
          Condition="Exists('$(OutputPath)..\OmniCard.ScannerHost\$(TargetFramework)\OmniCard.ScannerHost.exe')" />
  </Target>
```

Actually, since the TargetFramework differs (`net10.0-windows` vs `net10.0-windows10.0.22621.0`), a simpler approach: just reference the project normally and let MSBuild handle the output copy. Remove `ReferenceOutputAssembly=false`:

```xml
    <ProjectReference Include="..\OmniCard.ScannerHost\OmniCard.ScannerHost.csproj" />
```

This will build the host and copy its output to the main app's bin directory.

- [ ] **Step 2: Replace `Scan` with `ScanAsync` in ScannerService**

Replace the `Scan` method and remove the TWAIN transfer event handlers (they're no longer needed for scanning -- only discovery). Update `OmniCard.Scanner/ScannerService.cs`:

Replace the `Scan` method:

```csharp
    public async Task ScanAsync(bool showUI = false)
    {
        if (DataSource is null)
        {
            _logger.LogWarning("Scan requested but no data source is selected");
            return;
        }

        LastScanError = null;
        CardService.StartNewDiagnosticSession();

        var hostPath = Path.Combine(AppContext.BaseDirectory, "OmniCard.ScannerHost.exe");
        if (!File.Exists(hostPath))
        {
            LastScanError = "Scanner host not found. Please reinstall the application.";
            _logger.LogError("Scanner host not found at {Path}", hostPath);
            return;
        }

        var outputPath = Path.Combine(Path.GetTempPath(), $"omnicard_scan_{Guid.NewGuid()}.bmp");
        var dpi = ScanQuality == ScanQuality.Fast ? 200 : 300;

        var args = $"--scanner \"{DataSource.Name}\" --output \"{outputPath}\" --dpi {dpi}";
        if (showUI) args += " --show-ui";
        if (CardService.DefaultIsFoil) args += " --foil";

        _logger.LogInformation("Launching scanner host: {Args}", args);

        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            _logger.LogInformation("Scanner host exited with code {ExitCode}", process.ExitCode);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                _logger.LogInformation("Scan complete, processing image from {Path}", outputPath);
                using var stream = File.OpenRead(outputPath);
                CardService.AddFromStream(stream);
            }
            else
            {
                var reason = process.ExitCode switch
                {
                    1 => "Scanner not found.",
                    2 => $"Scanner driver error. {stderr}",
                    3 => "No image was received from the scanner.",
                    _ => $"Scanner process crashed (exit code {process.ExitCode})."
                };
                LastScanError = $"{reason}\n\nTry 'Import from Folder' as an alternative.";
                _logger.LogWarning("Scanner host failed: {Reason} stderr={StdErr}", reason, stderr);
            }
        }
        catch (Exception ex)
        {
            LastScanError = $"Failed to launch scanner: {ex.Message}\n\nTry 'Import from Folder' as an alternative.";
            _logger.LogError(ex, "Failed to launch scanner host process");
        }
        finally
        {
            // Clean up temp file
            try { if (File.Exists(outputPath)) File.Delete(outputPath); }
            catch { }
        }
    }
```

Remove the `Session_DataTransferred`, `Session_TransferReady`, `Session_TransferError`, and `Session_SourceDisabled` event handlers. Also remove the event subscriptions from the constructor and the unsubscriptions from `Dispose`. Remove the `LogCapabilities` and `ApplyScanSettings` methods and all the `TrySet*`/`TryReset*` helpers (they're now in the host).

The slimmed-down `ScannerService` keeps only:
- Constructor: create `TwainSession` and open it (for discovery)
- `EnsureSessionOpen()`
- `DataSource` property with `OnDataSourceChanged` (for connection dialog)
- `ScanAsync()` (launches host process)
- `LastScanError`
- `ScanQuality`, `WindowHandle` (WindowHandle no longer used for scanning but kept for backward compat)
- `CardService`
- `Dispose()`

Here is the complete replacement for `OmniCard.Scanner/ScannerService.cs`:

```csharp
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using NTwain;
using NTwain.Data;
using OmniCard.Interfaces;
using OmniCard.Models;
using System.Reflection;

namespace OmniCard.Scanner;

public sealed partial class ScannerService : ObservableObject, IDisposable
{
    private readonly ILogger<ScannerService> _logger;

    public TWIdentity AppID { get; }
    public TwainSession Session { get; }

    [ObservableProperty]
    public partial DataSource DataSource { get; set; }
    public ICardService CardService { get; }

    public ScanQuality ScanQuality { get; set; } = ScanQuality.Fast;
    public IntPtr WindowHandle { get; set; }

    public ScannerService(ICardService cardService, ILogger<ScannerService> logger)
    {
        _logger = logger;

        _logger.LogInformation("Initializing TWAIN session");
        AppID = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
        Session = new TwainSession(AppID);

        _logger.LogInformation("TWAIN session created (will open on first use)");
        CardService = cardService;
    }

    private bool _sessionOpened;

    public void EnsureSessionOpen()
    {
        if (_sessionOpened) return;
        try
        {
            Session.Open();
            _sessionOpened = true;
            _logger.LogInformation("TWAIN session opened successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open TWAIN session");
        }
    }

    public string? LastScanError { get; private set; }

    public async Task ScanAsync(bool showUI = false)
    {
        if (DataSource is null)
        {
            _logger.LogWarning("Scan requested but no data source is selected");
            return;
        }

        LastScanError = null;
        CardService.StartNewDiagnosticSession();

        var hostPath = Path.Combine(AppContext.BaseDirectory, "OmniCard.ScannerHost.exe");
        if (!File.Exists(hostPath))
        {
            LastScanError = "Scanner host not found. Please reinstall the application.";
            _logger.LogError("Scanner host not found at {Path}", hostPath);
            return;
        }

        var outputPath = Path.Combine(Path.GetTempPath(), $"omnicard_scan_{Guid.NewGuid()}.bmp");
        var dpi = ScanQuality == ScanQuality.Fast ? 200 : 300;

        var args = $"--scanner \"{DataSource.Name}\" --output \"{outputPath}\" --dpi {dpi}";
        if (showUI) args += " --show-ui";
        if (CardService.DefaultIsFoil) args += " --foil";

        _logger.LogInformation("Launching scanner host: {Args}", args);

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = !showUI,
            };

            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            _logger.LogInformation("Scanner host exited with code {ExitCode}", process.ExitCode);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                _logger.LogInformation("Scan complete, processing image from {Path}", outputPath);
                using var stream = File.OpenRead(outputPath);
                CardService.AddFromStream(stream);
            }
            else
            {
                var reason = process.ExitCode switch
                {
                    1 => "Scanner not found.",
                    2 => $"Scanner driver error. {stderr}",
                    3 => "No image was received from the scanner.",
                    _ => $"Scanner process crashed (exit code {process.ExitCode})."
                };
                LastScanError = $"{reason}\n\nTry 'Import from Folder' as an alternative.";
                _logger.LogWarning("Scanner host failed: {Reason} stderr={StdErr}", reason, stderr);
            }
        }
        catch (Exception ex)
        {
            LastScanError = $"Failed to launch scanner: {ex.Message}\n\nTry 'Import from Folder' as an alternative.";
            _logger.LogError(ex, "Failed to launch scanner host process");
        }
        finally
        {
            try { if (File.Exists(outputPath)) File.Delete(outputPath); }
            catch { }
        }
    }

    partial void OnDataSourceChanged(DataSource oldValue, DataSource newValue)
    {
        if (oldValue is not null)
        {
            _logger.LogInformation("Disconnecting from scanner: {OldSource}", oldValue.Name);
            oldValue.Close();
        }

        if (newValue is not null)
        {
            _logger.LogInformation("Connecting to scanner: {NewSource}", newValue.Name);
            newValue.Open();
            _logger.LogInformation("Connected to scanner: {NewSource}", newValue.Name);
        }
    }

    public void Dispose()
    {
        if (DataSource is not null && DataSource.IsOpen)
            DataSource.Close();

        if (_sessionOpened)
            Session.Close();
    }
}
```

- [ ] **Step 3: Update RootViewModel to use async ScanAsync**

In `OmniCard/Views/Root/RootViewModel.cs`, replace the `Scan()` command:

```csharp
    [RelayCommand]
    public async Task Scan()
    {
        if (IsAuditComplete) return;
        if (ConnectToScanner(false) ?? false)
        {
            _logger.LogInformation("User initiated scan");
            await ScannerService.ScanAsync(ShowScannerUI);
            if (ScannerService.LastScanError is not null)
            {
                Message = ScannerService.LastScanError;
                _logger.LogWarning("Scan failed: {Error}", ScannerService.LastScanError);
            }
        }
    }
```

- [ ] **Step 4: Build the full solution**

Run: `dotnet build OmniCard/OmniCard.csproj`
Expected: Build succeeds with 0 errors. `OmniCard.ScannerHost.exe` appears in the output directory.

- [ ] **Step 5: Verify the host exe is in output**

Run: `ls OmniCard/bin/Debug/net10.0-windows10.0.22621.0/win-x64/OmniCard.ScannerHost.exe`
Expected: File exists.

- [ ] **Step 6: Run all tests**

Run: `dotnet test OmniCard.Tests/OmniCard.Tests.csproj --no-restore`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add OmniCard/OmniCard.csproj OmniCard.Scanner/ScannerService.cs OmniCard/Views/Root/RootViewModel.cs
git commit -m "feat(scanner): rewire scanning to use out-of-process host for crash isolation"
```
