using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using NTwain;
using NTwain.Data;
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

        _logger.LogInformation("Starting scan on data source {DataSourceName}", DataSource.Name);
        DataSource.Enable(SourceEnableMode.NoUI, false, IntPtr.Zero);
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
