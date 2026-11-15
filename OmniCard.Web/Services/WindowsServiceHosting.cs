using System.Security.Cryptography.X509Certificates;

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
/// gives IIS, so the remote deploy script works unchanged: while app_offline.htm is in the app folder
/// the service stays down (its DLLs unlocked for the copy), and it comes back once the file is removed.
/// Only registered when running as a service.
///
/// <para>The restart is the Service Control Manager's own: the process exits with
/// <see cref="RestartExitCode"/> (not a clean stop), which counts as a failure, so the service's recovery
/// actions (install-service.ps1: restart after a few seconds, every time) start it again. A restart
/// that still finds the file exits again before touching anything (see <c>Program.cs</c>), until the
/// deploy removes it. This replaced a detached restart-helper process that could die or be refused
/// without a trace, leaving the service stopped after a deploy.</para>
/// </summary>
public sealed class AppOfflineWatcher(IHostEnvironment env, ILogger<AppOfflineWatcher> logger) : IHostedService, IDisposable
{
    public const string FileName = "app_offline.htm";

    /// <summary>Non-zero, so the SCM treats the exit as a failure and applies the recovery actions.</summary>
    public const int RestartExitCode = 1;

    /// <summary>True while a deploy is replacing the files in <paramref name="appDirectory"/>.</summary>
    public static bool IsOffline(string appDirectory) => File.Exists(Path.Combine(appDirectory, FileName));

    /// <summary>Ends the process. Replaced in tests.</summary>
    internal Action<int> Exit { get; init; } = Environment.Exit;

    private FileSystemWatcher? _watcher;
    private int _offline;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _watcher = new FileSystemWatcher(env.ContentRootPath, FileName) { NotifyFilter = NotifyFilters.FileName };
        _watcher.Created += (_, _) => GoOffline();
        _watcher.Renamed += (_, _) => GoOffline();
        _watcher.EnableRaisingEvents = true;
        // The file appeared after Program.cs checked for it: go offline now.
        GoOffline();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _watcher?.Dispose();

    private void GoOffline()
    {
        if (!IsOffline(env.ContentRootPath) || Interlocked.Exchange(ref _offline, 1) != 0)
            return;

        logger.LogWarning("{File} found: exiting so an update can replace the app's files. Windows restarts the service "
            + "(its recovery actions) once the file is removed.", FileName);
        // Off the caller's thread (a FileSystemWatcher callback, or host startup): Environment.Exit waits for
        // shutdown handlers, so it shouldn't run on a thread the host may be waiting on.
        _ = Task.Run(() => Exit(RestartExitCode));
    }
}
