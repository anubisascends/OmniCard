namespace OmniCard.Models;

public class AuditReport
{
    public required string LocationName { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public int ExpectedCount { get; init; }
    public int ActualCount { get; init; }
    public List<AuditReportItem> Matched { get; init; } = [];
    public List<AuditReportItem> Missing { get; init; } = [];
    public List<AuditReportItem> Extra { get; init; } = [];
}

public class AuditReportItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private string? _name;
    private string? _setCode;
    private string? _setName;
    private string? _collectorNumber;
    private string? _imageUri;
    private string? _gameCardId;
    private double? _confidence;
    private bool _isManuallyAssigned;

    public string? Name { get => _name; set => SetProperty(ref _name, value); }
    public string? SetCode { get => _setCode; set => SetProperty(ref _setCode, value); }
    public string? SetName { get => _setName; set => SetProperty(ref _setName, value); }
    public string? CollectorNumber { get => _collectorNumber; set => SetProperty(ref _collectorNumber, value); }
    public string? ImageUri { get => _imageUri; set => SetProperty(ref _imageUri, value); }
    public string? GameCardId { get => _gameCardId; set => SetProperty(ref _gameCardId, value); }
    public double? Confidence { get => _confidence; set => SetProperty(ref _confidence, value); }
    public bool IsManuallyAssigned { get => _isManuallyAssigned; set => SetProperty(ref _isManuallyAssigned, value); }

    /// <summary>Scan temp image path, for Extra items that came from the scanner.</summary>
    public string? ScanImagePath { get; init; }
}
