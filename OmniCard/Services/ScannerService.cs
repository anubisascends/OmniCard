using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using NTwain;
using NTwain.Data;
using OmniCard.Models;
using System.Reflection;

namespace OmniCard.Services;

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
        ApplyScanSettings(DataSource);
        _logger.LogInformation("Starting scan on data source {DataSourceName} (quality={Quality})",
            DataSource.Name, ScanQuality);
        DataSource.Enable(SourceEnableMode.NoUI, false, IntPtr.Zero);
    }

    private void ApplyScanSettings(DataSource ds)
    {
        var caps = ds.Capabilities;

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
