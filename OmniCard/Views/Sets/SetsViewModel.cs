using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.Sets;

/// <summary>Backs the read-only Sets tab: pick a game + set, see every printing sorted by
/// collector number with a check on the ones you own, and print a want-list of the rest.</summary>
public sealed partial class SetsViewModel : ObservableObject
{
    private readonly ISetChecklistService _checklistService;
    private readonly ISetChecklistPdfExporter _pdfExporter;
    private readonly IReadOnlyDictionary<CardGame, ICardGameService> _gameServices;
    private readonly ICardService _cardService;
    private readonly IDataPathService _dataPathService;
    private readonly ILogger<SetsViewModel> _logger;

    public SetsViewModel(
        ISetChecklistService checklistService,
        ISetChecklistPdfExporter pdfExporter,
        IEnumerable<ICardGameService> gameServices,
        ICardService cardService,
        IDataPathService dataPathService,
        ILogger<SetsViewModel> logger)
    {
        _checklistService = checklistService;
        _pdfExporter = pdfExporter;
        _gameServices = gameServices.ToDictionary(s => s.Game);
        _cardService = cardService;
        _dataPathService = dataPathService;
        _logger = logger;

        Games = new ObservableCollection<CardGame>(_cardService.AvailableGames);
        SelectedGame = Games.FirstOrDefault();
    }

    /// <summary>Data dir for the shared card-tile art resolver.</summary>
    public string DataDirectory => _dataPathService.DataDirectory;

    /// <summary>The card tiles never stack; the ×N quantity is drawn by the checklist overlay instead.</summary>
    public bool IsStacked => false;

    public ObservableCollection<CardGame> Games { get; }

    /// <summary>All sets for the selected game; <see cref="Sets"/> is this narrowed by the filter.</summary>
    private readonly List<SetInfo> _allSets = [];

    /// <summary>Sets shown in the picker, filtered by <see cref="SetFilterText"/>.</summary>
    public ObservableCollection<SetInfo> Sets { get; } = [];

    public ObservableCollection<SetChecklistCard> ChecklistCards { get; } = [];

    [ObservableProperty]
    public partial CardGame? SelectedGame { get; set; }

    [ObservableProperty]
    public partial SetInfo? SelectedSet { get; set; }

    [ObservableProperty]
    public partial string SetFilterText { get; set; } = "";

    [ObservableProperty]
    public partial string CompletionText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    /// <summary>The currently loaded checklist, or null until a set is loaded.</summary>
    public SetChecklist? Current { get; private set; }

    partial void OnSelectedGameChanged(CardGame? value)
    {
        LoadSets();
        Current = null;
        ChecklistCards.Clear();
        CompletionText = "";
        ExportWantListCommand.NotifyCanExecuteChanged();
    }

    partial void OnSetFilterTextChanged(string value) => ApplySetFilter();

    private void ApplySetFilter()
    {
        Sets.Clear();
        var filter = SetFilterText;
        foreach (var set in _allSets)
        {
            if (string.IsNullOrWhiteSpace(filter)
                || set.SetName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || set.SetCode.Contains(filter, StringComparison.OrdinalIgnoreCase))
                Sets.Add(set);
        }
    }

    private void LoadSets()
    {
        _allSets.Clear();
        if (SelectedGame is not null && _gameServices.TryGetValue(SelectedGame.Value, out var svc))
            _allSets.AddRange(svc.GetAvailableSets());
        ApplySetFilter();
    }

    [RelayCommand]
    private async Task LoadSetAsync()
    {
        if (SelectedGame is null || SelectedSet is null)
            return;

        var game = SelectedGame.Value;
        var code = SelectedSet.SetCode;

        IsLoading = true;
        StatusMessage = "";
        try
        {
            var checklist = await Task.Run(() => _checklistService.BuildAsync(game, code));

            Current = checklist;
            ChecklistCards.Clear();
            foreach (var card in checklist.Cards)
                ChecklistCards.Add(card);

            CompletionText = checklist.CompletionText;
            ExportWantListCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load set checklist for {Game} {Set}", game, code);
            StatusMessage = "Failed to load this set.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanExportWantList => Current is not null;

    [RelayCommand(CanExecute = nameof(CanExportWantList))]
    private void ExportWantList()
    {
        if (Current is null) return;

        var report = _checklistService.BuildWantListReport(Current);

        var safeName = string.Join("_", $"{report.SetName}".Split(Path.GetInvalidFileNameChars()));
        var dialog = new SaveFileDialog
        {
            FileName = $"{safeName}-want-list.pdf",
            Filter = "PDF files|*.pdf",
            DefaultExt = ".pdf",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            _pdfExporter.Export(report, dialog.FileName);
            StatusMessage = $"Saved want-list: {Path.GetFileName(dialog.FileName)} ({report.Rows.Count} cards)";
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export want-list for {Set}", report.SetCode);
            StatusMessage = "Failed to export the want-list PDF.";
        }
    }
}
