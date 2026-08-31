using System.Diagnostics;
using System.Globalization;
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

    // Per-scanner tuning (DPI, foil brightness/contrast) and arbitrary user-set capabilities now
    // come from the active scanner's saved profile; see BuildScanSettings/ApplyProfileCapabilities.
    private readonly IScannerProfileService _profileService;

    public ScannerService(ICardService cardService, IScannerProfileService profileService, ILogger<ScannerService> logger)
    {
        _logger = logger;
        _profileService = profileService;

        _logger.LogInformation("Initializing TWAIN session");

        AppID = new TWIdentity
        {
            Manufacturer = "Andrew Riebe",
            ProductFamily = "OmniCard",
            ProductName = "OmniCard Scanner",
            ProtocolMajor = TwainConst.ProtocolMajor,
            ProtocolMinor = TwainConst.ProtocolMinor,
            DataGroup = DataGroups.Image | DataGroups.Control,
            DataFunctionalities = DataFunctionalities.App2,
            Version = new TWVersion
            {
                Major = 1,
                Minor = 0,
                Language = Language.EnglishUSA,
                Country = Country.Usa,
                Info = "1.0.0"
            }
        };
        Session = new TwainSession(AppID);

        Session.TransferReady += Session_TransferReady;
        Session.DataTransferred += Session_DataTransferred;
        Session.TransferError += Session_TransferError;
        Session.SourceDisabled += Session_SourceDisabled;

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

        if (showUI)
        {
            // Out-of-process scan: isolates native driver crashes from the main app.
            // Used for network scanners whose TWAIN drivers crash in NoUI mode.
            await ScanOutOfProcessAsync();
        }
        else
        {
            // In-process scan: direct TWAIN call, works reliably for USB scanners.
            ScanInProcess();
        }
    }

    private void ScanInProcess()
    {
        LogCapabilities(DataSource);
        ApplyScanSettings(DataSource);
        ApplyProfileCapabilities(DataSource);
        _logger.LogInformation("Starting in-process scan on {DataSourceName} (quality={Quality})",
            DataSource.Name, ScanQuality);

        try
        {
            DataSource.Enable(SourceEnableMode.NoUI, false, WindowHandle);
        }
        catch (Exception ex)
        {
            LastScanError = $"Scanner driver error: {ex.Message}\n\nTry enabling 'Show Scanner UI' or use 'Import from Folder'.";
            _logger.LogError(ex, "Scanner driver threw an exception during Enable");
        }
    }

    private async Task ScanOutOfProcessAsync()
    {
        var hostPath = Path.Combine(AppContext.BaseDirectory, "OmniCard.ScannerHost.exe");
        if (!File.Exists(hostPath))
        {
            LastScanError = "Scanner host not found. Please reinstall the application.";
            _logger.LogError("Scanner host not found at {Path}", hostPath);
            return;
        }

        var outputPath = Path.Combine(Path.GetTempPath(), $"omnicard_scan_{Guid.NewGuid()}.bmp");
        var settings = BuildScanSettings();

        // Pass the resolved settings so the out-of-process host applies exactly what the
        // in-process path would (same DPI/foil tuning via the shared ScanSettingsApplier).
        // InvariantCulture so the host parses floats identically regardless of locale.
        var args = $"--scanner \"{DataSource.Name}\" --output \"{outputPath}\" --dpi {settings.Dpi} --show-ui";
        if (settings.Foil)
            args += $" --foil --foil-brightness {settings.FoilBrightness.ToString(CultureInfo.InvariantCulture)}" +
                    $" --foil-contrast {settings.FoilContrast.ToString(CultureInfo.InvariantCulture)}";

        // Hand the host the active scanner profile (its user-set capability layer) via a temp file,
        // so the out-of-process path applies the same caps the in-process path does. Written only
        // when there are caps to apply; cleaned up in the finally below.
        string? settingsPath = null;
        var activeProfile = GetActiveProfile();
        if (activeProfile is { Capabilities.Count: > 0 })
        {
            try
            {
                settingsPath = Path.Combine(Path.GetTempPath(), $"omnicard_profile_{Guid.NewGuid()}.json");
                File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(activeProfile));
                args += $" --settings \"{settingsPath}\"";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not write scanner profile for host; scanning without the capability layer");
                settingsPath = null;
            }
        }

        _logger.LogInformation("Launching scanner host: {Args}", args);

        // Close the data source and session so the host process can access
        // the scanner exclusively. NTwain can fault internally when Close() runs
        // on a source/session in an unexpected state (e.g. after a prior reopen
        // failed), so guard on IsOpen (mirroring Dispose) and treat these library
        // calls as untrusted (as the in-process Enable path does) — the host opens
        // its own TWAIN session, so a failed local close must not crash the scan.
        var scannerName = DataSource.Name;
        try
        {
            if (DataSource.IsOpen)
                DataSource.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to close TWAIN data source before launching scanner host; continuing");
        }
        if (_sessionOpened)
        {
            try
            {
                Session.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to close TWAIN session before launching scanner host; continuing");
            }
            _sessionOpened = false;
        }

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = false,
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

            try { if (settingsPath is not null && File.Exists(settingsPath)) File.Delete(settingsPath); }
            catch { }

            // Reopen session and data source for future scans / discovery
            try
            {
                EnsureSessionOpen();
                var source = Session.OfType<DataSource>()
                    .FirstOrDefault(s => string.Equals(s.Name, scannerName, StringComparison.OrdinalIgnoreCase));
                if (source is not null)
                {
                    source.Open();
                    DataSource = source;
                    _logger.LogDebug("Reopened scanner data source: {Name}", scannerName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reopen scanner after host scan");
            }
        }
    }

    private void LogCapabilities(DataSource ds)
    {
        var caps = ds.Capabilities;

        void LogCap(string name, Func<object?> getCurrent, Func<object?> getDefault, Func<object?> getRange, Func<bool> canSet)
        {
            try
            {
                _logger.LogInformation("CAP {Name}: current={Current}, default={Default}, range={Range}, canSet={CanSet}",
                    name, getCurrent(), getDefault(), getRange(), canSet());
            }
            catch (Exception ex) { _logger.LogDebug("CAP {Name}: not supported ({Error})", name, ex.Message); }
        }

        LogCap("Brightness", () => caps.ICapBrightness.GetCurrent(), () => caps.ICapBrightness.GetDefault(),
            () => { try { var r = caps.ICapBrightness.GetValues(); return r is not null ? string.Join(",", r.Take(10)) : "null"; } catch { return "N/A"; } },
            () => caps.ICapBrightness.CanSet);

        LogCap("Contrast", () => caps.ICapContrast.GetCurrent(), () => caps.ICapContrast.GetDefault(),
            () => { try { var r = caps.ICapContrast.GetValues(); return r is not null ? string.Join(",", r.Take(10)) : "null"; } catch { return "N/A"; } },
            () => caps.ICapContrast.CanSet);

        LogCap("Gamma", () => caps.ICapGamma.GetCurrent(), () => caps.ICapGamma.GetDefault(),
            () => { try { var r = caps.ICapGamma.GetValues(); return r is not null ? string.Join(",", r.Take(10)) : "null"; } catch { return "N/A"; } },
            () => caps.ICapGamma.CanSet);

        LogCap("Highlight", () => caps.ICapHighlight.GetCurrent(), () => caps.ICapHighlight.GetDefault(),
            () => "N/A", () => caps.ICapHighlight.CanSet);

        LogCap("Shadow", () => caps.ICapShadow.GetCurrent(), () => caps.ICapShadow.GetDefault(),
            () => "N/A", () => caps.ICapShadow.CanSet);

        LogCap("AutoBright", () => caps.ICapAutoBright.GetCurrent(), () => caps.ICapAutoBright.GetDefault(),
            () => "N/A", () => caps.ICapAutoBright.CanSet);

        LogCap("XResolution", () => caps.ICapXResolution.GetCurrent(), () => caps.ICapXResolution.GetDefault(),
            () => { try { var r = caps.ICapXResolution.GetValues(); return r is not null ? string.Join(",", r.Take(10)) : "null"; } catch { return "N/A"; } },
            () => caps.ICapXResolution.CanSet);

        try
        {
            _logger.LogInformation("CAP ExposureTime: canSet={CanSet}", caps.ICapExposureTime.CanSet);
            if (caps.ICapExposureTime.CanSet)
                _logger.LogInformation("CAP ExposureTime: current={Current}, default={Default}",
                    caps.ICapExposureTime.GetCurrent(), caps.ICapExposureTime.GetDefault());
        }
        catch (Exception ex) { _logger.LogDebug("CAP ExposureTime: not supported ({Error})", ex.Message); }

        try
        {
            _logger.LogInformation("CAP LightSource: canSet={CanSet}", caps.ICapLightSource.CanSet);
            if (caps.ICapLightSource.CanSet)
                _logger.LogInformation("CAP LightSource: current={Current}, default={Default}",
                    caps.ICapLightSource.GetCurrent(), caps.ICapLightSource.GetDefault());
        }
        catch (Exception ex) { _logger.LogDebug("CAP LightSource: not supported ({Error})", ex.Message); }

        try
        {
            _logger.LogInformation("CAP NoiseFilter: canSet={CanSet}", caps.ICapNoiseFilter.CanSet);
        }
        catch (Exception ex) { _logger.LogDebug("CAP NoiseFilter: not supported ({Error})", ex.Message); }

        try
        {
            _logger.LogInformation("CAP Duplex: mode={Mode}", caps.CapDuplex.GetCurrent());
        }
        catch (Exception ex) { _logger.LogDebug("CAP Duplex: not supported ({Error})", ex.Message); }

        try
        {
            _logger.LogInformation("CAP DuplexEnabled: current={Current}, canSet={CanSet}",
                caps.CapDuplexEnabled.GetCurrent(), caps.CapDuplexEnabled.CanSet);
        }
        catch (Exception ex) { _logger.LogDebug("CAP DuplexEnabled: not supported ({Error})", ex.Message); }

        try
        {
            _logger.LogInformation("CAP XferMech: current={Current}, canSet={CanSet}",
                caps.ICapXferMech.GetCurrent(), caps.ICapXferMech.CanSet);
            if (caps.ICapXferMech.CanSet)
            {
                var values = caps.ICapXferMech.GetValues();
                if (values is not null)
                    _logger.LogInformation("CAP XferMech: available={Available}", string.Join(",", values));
            }
        }
        catch (Exception ex) { _logger.LogDebug("CAP XferMech: not supported ({Error})", ex.Message); }
    }

    private void ApplyScanSettings(DataSource ds)
    {
        var settings = BuildScanSettings();
        ScanSettingsApplier.Apply(
            ds.Capabilities,
            settings,
            onInfo: msg => _logger.LogInformation("{ScanSetting}", msg),
            onDebug: msg => _logger.LogDebug("{ScanSetting}", msg));
    }

    /// <summary>Layer the active scanner profile's user-set capabilities on top of the baseline
    /// (applied after <see cref="ApplyScanSettings"/>). No-op when there's no profile / no caps.</summary>
    private void ApplyProfileCapabilities(DataSource ds)
    {
        var profile = GetActiveProfile();
        if (profile is null || profile.Capabilities.Count == 0) return;

        CapabilityProfileApplier.Apply(
            ds,
            profile.Capabilities,
            onDebug: msg => _logger.LogDebug("{CapSetting}", msg));
    }

    /// <summary>Resolve the current quality choice + the active scanner profile's tuning into the
    /// shared <see cref="ScanSettings"/>. Null profile values fall back to the shared defaults;
    /// a HighQualityDpi of 0 means "use the source's native default resolution".</summary>
    private ScanSettings BuildScanSettings()
    {
        var p = GetActiveProfile();
        return ScanSettings.Resolve(
            ScanQuality,
            CardService.DefaultIsFoil,
            p?.FastDpi ?? ScanSettings.DefaultFastDpi,
            p?.HighQualityDpi ?? ScanSettings.DefaultHighQualityDpi,
            (float)(p?.FoilBrightness ?? ScanSettings.DefaultFoilBrightness),
            (float)(p?.FoilContrast ?? ScanSettings.DefaultFoilContrast));
    }

    /// <summary>Load the saved profile for the connected source (null when nothing is connected
    /// or the profile can't be read — scanning then uses OmniCard's defaults).</summary>
    private ScannerProfile? GetActiveProfile()
    {
        if (DataSource is null) return null;
        try { return _profileService.GetProfile(DataSource.Name); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load scanner profile for {Name}", DataSource.Name);
            return null;
        }
    }

    private void Session_SourceDisabled(object? sender, EventArgs e)
    {
        _logger.LogDebug("Scanner source disabled");
    }

    private void Session_TransferError(object? sender, TransferErrorEventArgs e)
    {
        _logger.LogError("Scanner transfer error: {ErrorCode}", e.ReturnCode);
    }

    private void Session_DataTransferred(object? sender, DataTransferredEventArgs e)
    {
        _logger.LogInformation("Image data transferred from scanner, processing card");
        CardService.AddFromStream(e.GetNativeImageStream());
    }

    private void Session_TransferReady(object? sender, TransferReadyEventArgs e)
    {
        _logger.LogDebug("Scanner transfer ready");
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
        Session.TransferReady -= Session_TransferReady;
        Session.DataTransferred -= Session_DataTransferred;
        Session.TransferError -= Session_TransferError;
        Session.SourceDisabled -= Session_SourceDisabled;

        if (DataSource is not null && DataSource.IsOpen)
            DataSource.Close();

        if (_sessionOpened)
            Session.Close();
    }
}
