using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Root;

// Scan-session workflow: the scan tab requires an active session before any scanning; each session
// can be saved to / opened from a .ocss file, is continuously autosaved for crash recovery, and is
// closed (then replaced with a fresh one) when its cards are committed. See IScanSessionService.
public sealed partial class RootViewModel
{
    private bool _sessionDirty;
    private DispatcherTimer? _sessionAutosaveTimer;
    private static readonly TimeSpan AutosaveInterval = TimeSpan.FromSeconds(15);

    /// <summary>The open scan session, or null when none is active (which disables all scan controls).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveSession))]
    [NotifyPropertyChangedFor(nameof(ScanControlsEnabled))]
    [NotifyPropertyChangedFor(nameof(SessionStatusText))]
    [NotifyCanExecuteChangedFor(nameof(SaveScanSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveScanSessionAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseScanSessionCommand))]
    public partial ScanSession? CurrentSession { get; set; }

    public bool HasActiveSession => CurrentSession is not null;

    /// <summary>Scan-tab controls are usable only with an active session, except in audit mode which
    /// has its own lifecycle. Drives the IsEnabled of the scan toolbar + queue and the block overlay.</summary>
    public bool ScanControlsEnabled => HasActiveSession || IsAuditMode;

    // IsAuditMode lives in the main partial; keep the derived gate in sync when it toggles.
    partial void OnIsAuditModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ScanControlsEnabled));
    }

    /// <summary>Title-bar style status: session name plus a "•" dirty marker, or a prompt when none.</summary>
    public string SessionStatusText => CurrentSession is null
        ? "No scan session — click \"New Scan Session\" to begin"
        : $"Session: {CurrentSession.Name}{(CurrentSession.HasUnsavedChanges ? " •" : "")}";

    partial void OnCurrentSessionChanged(ScanSession? oldValue, ScanSession? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= OnSessionPropertyChanged;
        if (newValue is not null) newValue.PropertyChanged += OnSessionPropertyChanged;
        OnPropertyChanged(nameof(SessionStatusText));
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(nameof(SessionStatusText));

    // --- Session lifecycle commands ---

    [RelayCommand]
    private async Task NewScanSession()
    {
        if (IsAuditMode) return;
        if (!await ConfirmDiscardCurrentSessionAsync()) return;
        StartFreshSession();
        Message = "New scan session started.";
    }

    [RelayCommand]
    private async Task OpenScanSession()
    {
        if (IsAuditMode) return;
        if (!await ConfirmDiscardCurrentSessionAsync()) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Scan Session",
            Filter = scanSessionService.FileDialogFilter,
            InitialDirectory = SessionsDirectory(),
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var result = await scanSessionService.OpenAsync(dlg.FileName);
            LoadOpenedSession(result);
            Message = $"Opened session '{result.Session.Name}' ({result.Cards.Count} cards).";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open scan session {Path}", dlg.FileName);
            MessageBox.Show($"Could not open scan session:\n{ex.Message}", "Open Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveSession))]
    private async Task SaveScanSession()
    {
        await SaveCurrentSessionAsync(promptForPath: false);
    }

    [RelayCommand(CanExecute = nameof(HasActiveSession))]
    private async Task SaveScanSessionAs()
    {
        await SaveCurrentSessionAsync(promptForPath: true);
    }

    [RelayCommand(CanExecute = nameof(HasActiveSession))]
    private async Task CloseScanSession()
    {
        if (IsAuditMode) return;
        if (!await ConfirmDiscardCurrentSessionAsync()) return;
        CloseSessionAndClearQueue();
        scanSessionService.ClearRecovery();
        Message = "Scan session closed.";
    }

    // --- Helpers ---

    /// <summary>Called when the scan queue changes or a card is edited: flags the session dirty so
    /// the periodic autosave persists it and the status marker updates.</summary>
    public void OnScanSessionMutated()
    {
        if (CurrentSession is null) return;
        _sessionDirty = true;
        CurrentSession.HasUnsavedChanges = true;
    }

    /// <summary>After a successful commit the data is safely in the DB: discard the recovery autosave
    /// and open a fresh empty session so scanning can continue.</summary>
    private void OnScansCommitted()
    {
        scanSessionService.ClearRecovery();
        StartFreshSession();
    }

    private void StartFreshSession()
    {
        // Replace the queue with an empty one belonging to the new session.
        CardService.ClearTempFiles();
        CardService.ScannedCards.Clear();
        CurrentSession = new ScanSession();
        _sessionDirty = false;
        EnsureAutosaveTimer();
    }

    private void LoadOpenedSession(ScanSessionOpenResult result)
    {
        CardService.ClearTempFiles();
        CardService.ScannedCards.Clear();
        foreach (var card in result.Cards)
        {
            CardService.AnnotateScan(card);   // refresh first-copy / current-price for display
            CardService.ScannedCards.Add(card);
        }
        CurrentSession = result.Session;
        _sessionDirty = false;
        EnsureAutosaveTimer();
    }

    private void CloseSessionAndClearQueue()
    {
        CardService.ClearTempFiles();
        CardService.ScannedCards.Clear();
        CurrentSession = null;
        _sessionDirty = false;
    }

    private async Task<bool> SaveCurrentSessionAsync(bool promptForPath)
    {
        if (CurrentSession is null) return false;

        var path = CurrentSession.FilePath;
        if (promptForPath || path is null)
        {
            var dir = SessionsDirectory();
            Directory.CreateDirectory(dir);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Scan Session",
                Filter = scanSessionService.FileDialogFilter,
                InitialDirectory = dir,
                FileName = CurrentSession.HasBeenSaved
                    ? Path.GetFileName(CurrentSession.FilePath)
                    : $"scan-session-{DateTime.Now:yyyy-MM-dd-HHmm}{scanSessionService.FileExtension}",
                DefaultExt = scanSessionService.FileExtension,
                AddExtension = true,
            };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }

        if (path is null) return false;

        try
        {
            var cards = CardService.ScannedCards.ToList();
            await scanSessionService.SaveAsync(CurrentSession, cards, path);
            CurrentSession.FilePath = path;
            CurrentSession.Name = Path.GetFileNameWithoutExtension(path);
            CurrentSession.HasUnsavedChanges = false;
            _sessionDirty = false;
            OnPropertyChanged(nameof(SessionStatusText));
            Message = $"Saved session to {Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save scan session to {Path}", path);
            MessageBox.Show($"Could not save scan session:\n{ex.Message}", "Save Failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Prompt-to-save guard before discarding the current session (new/open/close/exit).
    /// Returns true to proceed, false to cancel the operation.</summary>
    private async Task<bool> ConfirmDiscardCurrentSessionAsync()
    {
        if (CurrentSession is null || CardService.ScannedCards.Count == 0 || !CurrentSession.HasUnsavedChanges)
            return true;

        var choice = MessageBox.Show(
            $"Save changes to '{CurrentSession.Name}' before continuing?",
            "Unsaved Scan Session",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return choice switch
        {
            MessageBoxResult.Yes => await SaveCurrentSessionAsync(promptForPath: false),
            MessageBoxResult.No => true,
            _ => false, // Cancel / closed
        };
    }

    private void PromptStartSession()
        => Message = "Start a new scan session (or open one) before scanning.";

    /// <summary>Synchronous app-exit guard (window is closing). Returns true to allow the exit,
    /// false to cancel it. On "Save" the session is written before the app closes.</summary>
    public bool ConfirmExitWithUnsavedSession()
    {
        if (CurrentSession is null || CardService.ScannedCards.Count == 0 || !CurrentSession.HasUnsavedChanges)
            return true;

        var choice = MessageBox.Show(
            $"Save changes to '{CurrentSession.Name}' before exiting?\n\n(The session is also auto-saved and can be recovered next launch.)",
            "Unsaved Scan Session",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return choice switch
        {
            MessageBoxResult.Cancel => false,
            MessageBoxResult.No => true,
            _ => SaveForExit(),
        };
    }

    private bool SaveForExit()
    {
        var path = CurrentSession!.FilePath;
        if (path is null)
        {
            var dir = SessionsDirectory();
            Directory.CreateDirectory(dir);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Scan Session",
                Filter = scanSessionService.FileDialogFilter,
                InitialDirectory = dir,
                FileName = $"scan-session-{DateTime.Now:yyyy-MM-dd-HHmm}{scanSessionService.FileExtension}",
                DefaultExt = scanSessionService.FileExtension,
                AddExtension = true,
            };
            if (dlg.ShowDialog() != true) return false; // cancelled the save → cancel the exit
            path = dlg.FileName;
        }

        try
        {
            var cards = CardService.ScannedCards.ToList();
            // Run off the UI thread and block: the write's async continuation doesn't need the UI
            // dispatcher (which is what would otherwise deadlock a .Result on the UI thread).
            Task.Run(() => scanSessionService.SaveAsync(CurrentSession!, cards, path)).GetAwaiter().GetResult();
            CurrentSession!.FilePath = path;
            CurrentSession.HasUnsavedChanges = false;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save scan session on exit");
            return MessageBox.Show("Could not save the scan session. Exit anyway?", "Save Failed",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }
    }

    private string SessionsDirectory() => dataPathService.SessionsDirectory;

    // --- Autosave (crash recovery) ---

    private void EnsureAutosaveTimer()
    {
        if (_sessionAutosaveTimer is not null) return;
        _sessionAutosaveTimer = new DispatcherTimer { Interval = AutosaveInterval };
        _sessionAutosaveTimer.Tick += (_, _) => AutosaveTick();
        _sessionAutosaveTimer.Start();
    }

    private void AutosaveTick()
    {
        if (CurrentSession is null) return;

        // Nothing worth recovering: keep the recovery file empty/absent.
        if (CardService.ScannedCards.Count == 0)
        {
            if (_sessionDirty) { scanSessionService.ClearRecovery(); _sessionDirty = false; }
            return;
        }

        if (!_sessionDirty) return;
        _sessionDirty = false;

        var session = CurrentSession;
        var cards = CardService.ScannedCards.ToList(); // snapshot on the UI thread
        _ = AutosaveInBackgroundAsync(session, cards);
    }

    private async Task AutosaveInBackgroundAsync(ScanSession session, List<ScannedCard> cards)
    {
        try { await scanSessionService.AutosaveAsync(session, cards); }
        catch (Exception ex) { _logger.LogWarning(ex, "Scan session autosave failed"); }
    }

    /// <summary>On startup, offer to restore an unsaved session left by a previous run (e.g. a crash).</summary>
    public async Task CheckScanSessionRecoveryAsync()
    {
        try
        {
            if (!scanSessionService.TryGetRecoverable(out var savedUtc)) return;

            var choice = MessageBox.Show(
                $"An unsaved scan session from {savedUtc.ToLocalTime():g} was found.\n\nRestore it?",
                "Recover Scan Session",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (choice == MessageBoxResult.Yes)
            {
                var result = await scanSessionService.RecoverAsync();
                LoadOpenedSession(result);
                // A recovered session has unsaved changes by definition (it was never saved to a file).
                OnScanSessionMutated();
                Message = $"Recovered scan session ({result.Cards.Count} cards).";
            }
            else
            {
                scanSessionService.ClearRecovery();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Scan session recovery check failed");
        }
    }
}
