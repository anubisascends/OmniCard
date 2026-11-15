using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Web.Services;

namespace OmniCard.Tests.Web;

/// <summary>
/// The Windows-service side of the app_offline.htm deploy contract: while the file is in the app folder
/// the process must exit with a FAILURE code, because that is what makes the Service Control Manager's
/// recovery actions start the service again (a clean stop leaves it stopped after the deploy).
/// </summary>
public sealed class AppOfflineWatcherTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("omnicard-appoffline-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class Env(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "OmniCard.Web";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private (AppOfflineWatcher Watcher, TaskCompletionSource<int> Exited) NewWatcher()
    {
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new AppOfflineWatcher(new Env(_dir), NullLogger<AppOfflineWatcher>.Instance)
        {
            Exit = code => exited.TrySetResult(code),
        };
        return (watcher, exited);
    }

    private void CreateMarker() => File.WriteAllText(Path.Combine(_dir, AppOfflineWatcher.FileName), "updating");

    [Fact]
    public void IsOffline_FollowsTheMarkerFile()
    {
        Assert.False(AppOfflineWatcher.IsOffline(_dir));
        CreateMarker();
        Assert.True(AppOfflineWatcher.IsOffline(_dir));
    }

    [Fact]
    public async Task MarkerPresentAtStart_ExitsWithFailureCode()
    {
        CreateMarker();
        var (watcher, exited) = NewWatcher();
        using (watcher)
        {
            await watcher.StartAsync(CancellationToken.None);
            var code = await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(AppOfflineWatcher.RestartExitCode, code);
            Assert.NotEqual(0, code);
        }
    }

    [Fact]
    public async Task MarkerCreatedWhileRunning_ExitsWithFailureCode()
    {
        var (watcher, exited) = NewWatcher();
        using (watcher)
        {
            await watcher.StartAsync(CancellationToken.None);
            Assert.False(exited.Task.IsCompleted);

            CreateMarker();
            var code = await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(AppOfflineWatcher.RestartExitCode, code);
        }
    }

    [Fact]
    public async Task NoMarker_KeepsRunning()
    {
        var (watcher, exited) = NewWatcher();
        using (watcher)
        {
            await watcher.StartAsync(CancellationToken.None);
            await Task.Delay(500);
            Assert.False(exited.Task.IsCompleted);
        }
    }
}
