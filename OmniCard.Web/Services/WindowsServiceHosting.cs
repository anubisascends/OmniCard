using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace OmniCard.Web.Services;

/// <summary>
/// Helpers for hosting the app as a Windows service on Kestrel (see install-service.ps1) instead of
/// under IIS.
/// </summary>
public static class WindowsServiceHosting
{
    /// <summary>
    /// Loads the HTTPS certificate from the LocalMachine\My store by thumbprint. The service account
    /// needs read access to the certificate's private key; install-service.ps1 grants it.
    /// </summary>
    public static X509Certificate2 LoadMachineCertificate(string thumbprint)
    {
        var normalized = new string(thumbprint.Where(char.IsAsciiHexDigit).ToArray());
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, validOnly: false);
        return matches.Count > 0
            ? matches[0]
            : throw new InvalidOperationException(
                $"HttpsCertificateThumbprint: no certificate {normalized} in the LocalMachine\\My store.");
    }
}

/// <summary>
/// Gives a Windows-service install the same app_offline.htm deploy contract the ASP.NET Core Module
/// gives IIS, so the remote deploy script works unchanged: when app_offline.htm appears in the app
/// folder, the service stops (releasing its DLLs for the copy), and a detached helper starts it again
/// once the file is removed. Only registered when running as a service.
/// </summary>
public sealed class AppOfflineWatcher(
    IHostEnvironment env,
    IHostApplicationLifetime lifetime,
    IConfiguration config,
    ILogger<AppOfflineWatcher> logger) : IHostedService, IDisposable
{
    public const string FileName = "app_offline.htm";

    private FileSystemWatcher? _watcher;
    private int _offline;

    private string MarkerPath => Path.Combine(env.ContentRootPath, FileName);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _watcher = new FileSystemWatcher(env.ContentRootPath, FileName) { NotifyFilter = NotifyFilters.FileName };
        _watcher.Created += (_, _) => GoOffline();
        _watcher.Renamed += (_, _) => GoOffline();
        _watcher.EnableRaisingEvents = true;
        // Started (by a reboot, or by hand) while a deploy is still copying: stay offline.
        if (File.Exists(MarkerPath))
            GoOffline();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _watcher?.Dispose();

    private void GoOffline()
    {
        if (!File.Exists(MarkerPath) || Interlocked.Exchange(ref _offline, 1) != 0)
            return;

        var serviceName = config["WindowsService:Name"] ?? "OmniCard";
        logger.LogWarning("{File} found: stopping service {Service} for an update. It restarts once the file is removed.",
            FileName, serviceName);
        try
        {
            StartRestartHelper(serviceName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Couldn't start the restart helper. Start service {Service} by hand after the update.", serviceName);
        }
        lifetime.StopApplication();
    }

    /// <summary>
    /// Launches a detached PowerShell process (from System32, so it locks nothing in the app folder) that
    /// waits for this process to exit, then for the marker to go, then starts the service. It runs as
    /// the service account, which install-service.ps1 allows to start its own service.
    /// </summary>
    private void StartRestartHelper(string serviceName)
    {
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        var script = $$"""
            Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
            while (Test-Path -LiteralPath {{Quote(MarkerPath)}}) { Start-Sleep -Seconds 2 }
            Start-Sleep -Seconds 2
            Start-Service -Name {{Quote(serviceName)}}
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.SystemDirectory,
        })?.Dispose();
    }
}
