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

    public ScannerService(ICardService cardService, ILogger<ScannerService> logger)
    {
        _logger = logger;

        _logger.LogInformation("Initializing TWAIN session");
        AppID = TWIdentity.CreateFromAssembly(DataGroups.Image, Assembly.GetExecutingAssembly());
        Session = new TwainSession(AppID);

        Session.TransferReady += Session_TransferReady;
        Session.DataTransferred += Session_DataTransferred;
        Session.TransferError += Session_TransferError;
        Session.SourceDisabled += Session_SourceDisabled;

        // Defer TWAIN session open — it can hang if the scanner driver is locked
        // or not installed. The session is opened lazily on first Scan() call.
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

    public void Scan()
    {
        if (DataSource is null)
        {
            _logger.LogWarning("Scan requested but no data source is selected");
            return;
        }

        CardService.StartNewDiagnosticSession();
        LogCapabilities(DataSource);
        ApplyScanSettings(DataSource);
        _logger.LogInformation("Starting scan on data source {DataSourceName} (quality={Quality})",
            DataSource.Name, ScanQuality);
        DataSource.Enable(SourceEnableMode.NoUI, false, IntPtr.Zero);
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

        // Log any exposure-related caps
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

    }

    private void ApplyScanSettings(DataSource ds)
    {
        var caps = ds.Capabilities;

        // Always set 24-bit color and sRGB for consistent scans
        TrySetPixelType(caps);
        TrySetColorProfile(caps);
        TryDisableDuplex(caps);

        if (ScanQuality == ScanQuality.Fast)
        {
            TrySetResolution(caps, 200f);
            TryResetImageProcessing(caps);
        }
        else
        {
            // HighQuality — reset everything to scanner driver defaults
            TryResetResolution(caps);
            TryResetImageProcessing(caps);
        }

        // Foil cards over-reflect the scanner light source, causing washed-out
        // images and confusing the scanner's automatic edge detection.
        // Reduce brightness and disable auto-brightness to tame the reflection.
        if (CardService.DefaultIsFoil)
        {
            TrySetAutoBright(caps, false);
            TrySetBrightness(caps, -200f);
            TrySetContrast(caps, 333.3333f);
            _logger.LogInformation("Foil mode: brightness reduced, auto-bright disabled, contrast boosted");
        }
    }

    private void TrySetResolution(ICapabilities caps, float dpi)
    {
        try { if (caps.ICapXResolution.CanSet) caps.ICapXResolution.SetValue((TWFix32)dpi); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot set XResolution"); }

        try { if (caps.ICapYResolution.CanSet) caps.ICapYResolution.SetValue((TWFix32)dpi); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot set YResolution"); }
    }

    private void TryResetResolution(ICapabilities caps)
    {
        try { if (caps.ICapXResolution.CanReset) caps.ICapXResolution.Reset(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot reset XResolution"); }

        try { if (caps.ICapYResolution.CanReset) caps.ICapYResolution.Reset(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot reset YResolution"); }
    }

    private void TrySetBrightness(ICapabilities caps, float value)
    {
        try { if (caps.ICapBrightness.CanSet) caps.ICapBrightness.SetValue((TWFix32)value); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot set Brightness to {Value}", value); }
    }

    private void TrySetContrast(ICapabilities caps, float value)
    {
        try { if (caps.ICapContrast.CanSet) caps.ICapContrast.SetValue((TWFix32)value); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot set Contrast to {Value}", value); }
    }

    private void TrySetAutoBright(ICapabilities caps, bool enabled)
    {
        try { if (caps.ICapAutoBright.CanSet) caps.ICapAutoBright.SetValue(enabled ? BoolType.True : BoolType.False); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot set AutoBright to {Enabled}", enabled); }
    }

    private void TryResetImageProcessing(ICapabilities caps)
    {
        try { if (caps.ICapAutoBright.CanReset) caps.ICapAutoBright.Reset(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot reset AutoBright"); }

        try { if (caps.ICapBrightness.CanReset) caps.ICapBrightness.Reset(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot reset Brightness"); }

        try { if (caps.ICapContrast.CanReset) caps.ICapContrast.Reset(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot reset Contrast"); }

        try { if (caps.ICapGamma.CanReset) caps.ICapGamma.Reset(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot reset Gamma"); }

        try { if (caps.ICapHighlight.CanReset) caps.ICapHighlight.Reset(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot reset Highlight"); }

        try { if (caps.ICapShadow.CanReset) caps.ICapShadow.Reset(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot reset Shadow"); }
    }

    private void TrySetPixelType(ICapabilities caps)
    {
        try { if (caps.ICapPixelType.CanSet) caps.ICapPixelType.SetValue(PixelType.RGB); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot set PixelType to RGB"); }
    }

    private void TrySetColorProfile(ICapabilities caps)
    {
        // Embed the scanner's ICC profile for consistent color management.
        // For sRGB output, the scanner driver itself must be configured to use sRGB.
        try { if (caps.ICapICCProfile.CanSet) caps.ICapICCProfile.SetValue(IccProfile.Embed); }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot set ICC profile"); }
    }

    private void TryDisableDuplex(ICapabilities caps)
    {
        try
        {
            if (caps.CapDuplexEnabled.CanSet)
            {
                caps.CapDuplexEnabled.SetValue(BoolType.False);
                _logger.LogInformation("Duplex scanning disabled (single-sided)");
            }
            else
            {
                _logger.LogDebug("Scanner does not support setting duplex mode");
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Cannot disable duplex"); }
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
        if(oldValue is not null)
        {
            _logger.LogInformation("Disconnecting from scanner: {OldSource}", oldValue.Name);
            oldValue.Close();
        }

        if(newValue is not null)
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
