using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;

namespace OmniCard.Views.DataLocation;

public sealed partial class DataLocationViewModel(
    IDataPathService dataPathService,
    IDataMigrationService migrationService) : ViewModel
{
    [ObservableProperty]
    public partial string CurrentPath { get; set; } = dataPathService.DataDirectory;

    [ObservableProperty]
    public partial string? PendingPath { get; set; } = dataPathService.PendingDataDirectory;

    [ObservableProperty]
    public partial bool IsMigrationPending { get; set; } = dataPathService.IsMigrationPending;

    [ObservableProperty]
    public partial string StatusText { get; set; } = dataPathService.IsMigrationPending ? "Migration pending" : "Active";

    [ObservableProperty]
    public partial bool IsMigrating { get; set; }

    [ObservableProperty]
    public partial double MigrationProgress { get; set; }

    [ObservableProperty]
    public partial string MigrationStatusText { get; set; } = "";

    [ObservableProperty]
    public partial string DataSummary { get; set; } = "";

    public Action? CloseDialog { get; set; }

    private CancellationTokenSource? _migrationCts;

    public async Task LoadAsync()
    {
        var plan = await migrationService.PrepareMigrationAsync();
        DataSummary = FormatDataSummary(plan);
    }

    [RelayCommand]
    public void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select data directory",
            InitialDirectory = CurrentPath,
        };

        if (dialog.ShowDialog() != true)
            return;

        var selectedPath = dialog.FolderName;

        // Don't allow selecting the current data directory
        if (string.Equals(Path.GetFullPath(selectedPath), Path.GetFullPath(CurrentPath), StringComparison.OrdinalIgnoreCase))
            return;

        // Warn if non-empty and not already a OmniCard directory. The current store is
        // inventory.db; collection.db is the legacy pre-migration name (absent once migrated).
        if (Directory.Exists(selectedPath) &&
            Directory.EnumerateFileSystemEntries(selectedPath).Any() &&
            !File.Exists(Path.Combine(selectedPath, "inventory.db")) &&
            !File.Exists(Path.Combine(selectedPath, "collection.db")))
        {
            var result = System.Windows.MessageBox.Show(
                "The selected folder is not empty and does not appear to be a OmniCard data directory. Continue?",
                "Warning",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;
        }

        dataPathService.SetPendingDataDirectory(selectedPath);
        PendingPath = selectedPath;
        IsMigrationPending = true;
        StatusText = "Migration pending";
    }

    [RelayCommand]
    public async Task MigrateAsync()
    {
        if (!dataPathService.IsMigrationPending)
            return;

        var plan = await migrationService.PrepareMigrationAsync();

        var confirm = System.Windows.MessageBox.Show(
            $"Migrate {plan.FileCount} files ({FormatBytes(plan.TotalBytes)}) to:\n{PendingPath}\n\n" +
            (plan.Warnings.Count > 0 ? string.Join("\n", plan.Warnings) + "\n\n" : "") +
            "Continue?",
            "Confirm Migration",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (confirm != System.Windows.MessageBoxResult.Yes)
            return;

        IsMigrating = true;
        MigrationProgress = 0;
        MigrationStatusText = "Starting migration...";
        _migrationCts = new CancellationTokenSource();

        var progress = new Progress<MigrationProgress>(p =>
        {
            MigrationProgress = p.TotalBytes > 0 ? (double)p.BytesCopied / p.TotalBytes * 100 : 100;
            MigrationStatusText = $"[{p.FilesCompleted}/{p.TotalFiles}] {p.CurrentFile}";
        });

        var result = await migrationService.ExecuteMigrationAsync(progress, _migrationCts.Token);

        IsMigrating = false;
        _migrationCts?.Dispose();
        _migrationCts = null;

        if (result.Success)
        {
            CurrentPath = dataPathService.DataDirectory;
            PendingPath = null;
            IsMigrationPending = false;
            StatusText = "Active";
            DataSummary = "";
            var restart = System.Windows.MessageBox.Show(
                "Migration complete. The app must restart to use the new location.\n\nRestart now?",
                "Migration Complete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);

            if (restart == System.Windows.MessageBoxResult.Yes)
            {
                var exePath = Environment.ProcessPath;
                if (exePath is not null)
                    System.Diagnostics.Process.Start(exePath);
                System.Windows.Application.Current.Shutdown();
            }
        }
        else
        {
            System.Windows.MessageBox.Show(
                $"Migration failed: {result.ErrorMessage}",
                "Migration Failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void CancelMigration()
    {
        if (IsMigrating)
        {
            _migrationCts?.Cancel();
            return;
        }

        dataPathService.CancelPendingMigration();
        PendingPath = null;
        IsMigrationPending = false;
        StatusText = "Active";
    }

    [RelayCommand]
    public void Close()
    {
        CloseDialog?.Invoke();
    }

    private static string FormatDataSummary(MigrationPlan plan)
    {
        if (plan.FileCount == 0)
            return "No data files found.";
        return $"{plan.FileCount} files, {FormatBytes(plan.TotalBytes)}";
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
    };
}
