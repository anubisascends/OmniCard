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
