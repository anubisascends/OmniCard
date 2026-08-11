using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Settings;

/// <summary>
/// Backs the Settings page's "Scan Workflow" section: toggling between the Store (keep
/// scans) and Discard (delete scans after commit) workflows, persisted via
/// <see cref="IScannerSettingsService"/>, plus the archive/import actions backed by
/// <see cref="IScanArchiveService"/>.
/// </summary>
public partial class ScannerSettingsViewModel(
    IScannerSettingsService scannerSettings,
    IScanArchiveService scanArchive) : ObservableObject
{
    private bool _suppressChangeHandler;

    [ObservableProperty]
    public partial ScanWorkflowMode WorkflowMode { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public void Load()
    {
        _suppressChangeHandler = true;
        WorkflowMode = scannerSettings.WorkflowMode;
        _suppressChangeHandler = false;
    }

    partial void OnWorkflowModeChanged(ScanWorkflowMode oldValue, ScanWorkflowMode newValue)
    {
        if (_suppressChangeHandler || oldValue == newValue) return;

        if (oldValue == ScanWorkflowMode.Store && newValue == ScanWorkflowMode.Discard)
        {
            var result = MessageBox.Show(
                "Switching to Discard deletes each scan's image after it's committed — nothing is kept in the scans folder.\n\n" +
                "Archive the scans you already have before switching?",
                "Archive Current Scans",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _ = ArchiveThenPersist(newValue);
                return;
            }
        }

        scannerSettings.SetWorkflowMode(newValue);
        StatusMessage = $"Scan workflow set to {newValue}.";
    }

    private async Task ArchiveThenPersist(ScanWorkflowMode newValue)
    {
        IsBusy = true;
        StatusMessage = "Archiving current scans...";
        var progress = new Progress<string>(msg => Application.Current.Dispatcher.Invoke(() => StatusMessage = msg));

        var result = await scanArchive.ArchiveCurrentScansAsync(progress);
        IsBusy = false;

        if (!result.Success)
        {
            MessageBox.Show(
                $"Archive failed: {result.ErrorMessage}\n\nThe scan workflow was not changed.",
                "Archive Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            _suppressChangeHandler = true;
            WorkflowMode = ScanWorkflowMode.Store;
            _suppressChangeHandler = false;
            return;
        }

        scannerSettings.SetWorkflowMode(newValue);
        StatusMessage = result.ImageCount > 0
            ? $"Archived {result.ImageCount} scans to {result.ArchivePath}. Workflow set to Discard."
            : "No scans to archive. Workflow set to Discard.";
    }

    [RelayCommand]
    public async Task ImportArchive()
    {
        var dialog = new OpenFileDialog { Title = "Select scan archive", Filter = "Zip archives|*.zip" };
        if (dialog.ShowDialog() != true) return;

        IsBusy = true;
        StatusMessage = "Importing archive...";
        var progress = new Progress<string>(msg => Application.Current.Dispatcher.Invoke(() => StatusMessage = msg));

        var result = await scanArchive.ImportArchiveAsync(dialog.FileName, progress);
        IsBusy = false;

        if (!result.Success)
        {
            MessageBox.Show($"Import failed: {result.ErrorMessage}", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        StatusMessage = $"Extracted {result.ImagesExtracted} images. Linked {result.LinkedToLots} lots. {result.Orphaned} orphaned.";

        if (result.Orphaned > 0)
        {
            var names = string.Join("\n", result.OrphanedFileNames.Take(20));
            if (result.OrphanedFileNames.Count > 20)
                names += $"\n...and {result.OrphanedFileNames.Count - 20} more";

            MessageBox.Show(
                $"{result.Orphaned} image(s) were extracted but could not be relinked (their inventory lot no longer exists):\n\n{names}",
                "Import Summary — Orphaned Scans",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
