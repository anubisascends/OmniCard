using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Models;
using OmniCard.Services;

// Alias the model types to avoid collision with the OmniCard.Views.AuditReport namespace
using AuditReportModel = OmniCard.Models.AuditReport;
using AuditReportItemModel = OmniCard.Models.AuditReportItem;

namespace OmniCard.Views.AuditReport;

public sealed partial class AuditReportViewModel(ICardService cardService) : ObservableObject
{
    [ObservableProperty]
    public partial AuditReportModel? Report { get; set; }

    [ObservableProperty]
    public partial string ManualSearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial List<CardMatch>? SearchResults { get; set; }

    [ObservableProperty]
    public partial AuditReportItemModel? SelectedItemForAssignment { get; set; }

    public string MatchRateText => Report is not null && Report.ExpectedCount > 0
        ? $"{(double)Report.Matched.Count / Report.ExpectedCount * 100:F1}%"
        : "N/A";

    public void Load(AuditReportModel report)
    {
        Report = report;
        OnPropertyChanged(nameof(MatchRateText));
    }

    [RelayCommand]
    public void SearchForAssignment()
    {
        if (string.IsNullOrWhiteSpace(ManualSearchQuery) || SelectedItemForAssignment is null)
            return;

        var results = cardService.ActiveGameService.SearchCards(ManualSearchQuery, 20);
        SearchResults = results;
    }

    [RelayCommand]
    public void AssignCard(CardMatch match)
    {
        if (SelectedItemForAssignment is null) return;

        SelectedItemForAssignment.Name = match.Name;
        SelectedItemForAssignment.SetCode = match.SetCode;
        SelectedItemForAssignment.SetName = match.SetName;
        SelectedItemForAssignment.CollectorNumber = match.CollectorNumber;
        SelectedItemForAssignment.GameCardId = match.GameSpecificId;
        SelectedItemForAssignment.ImageUri = match.ImageUri;
        SelectedItemForAssignment.IsManuallyAssigned = true;

        // Clear search state
        SelectedItemForAssignment = null;
        ManualSearchQuery = "";
        SearchResults = null;
    }

    [RelayCommand]
    public void BeginAssignment(AuditReportItemModel item)
    {
        SelectedItemForAssignment = item;
        ManualSearchQuery = "";
        SearchResults = null;
    }

    [RelayCommand]
    public void CancelAssignment()
    {
        SelectedItemForAssignment = null;
        ManualSearchQuery = "";
        SearchResults = null;
    }

    /// <summary>Called by the PDF export button.</summary>
    public Action? ExportPdf { get; set; }

    [RelayCommand]
    public void ExportToPdf() => ExportPdf?.Invoke();
}
