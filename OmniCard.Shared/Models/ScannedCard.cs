using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniCard.Models;

public partial class ScannedCard : ObservableObject
{
    public required string TempImagePath { get; init; }
    public required ulong Hash { get; set; }
    public ulong[]? ArtHashes { get; init; }

    [ObservableProperty]
    public partial CardGame Game { get; set; }

    [ObservableProperty]
    public partial CardMatch? Match { get; set; }

    [ObservableProperty]
    public partial string Condition { get; set; } = "NM";

    [ObservableProperty]
    public partial bool IsFoil { get; set; }

    [ObservableProperty]
    public partial decimal? PurchasePrice { get; set; }

    // Per-card location overrides (takes precedence over toolbar defaults when set)
    [ObservableProperty]
    public partial StorageContainer? OverrideContainer { get; set; }

    [ObservableProperty]
    public partial int? OverridePage { get; set; }

    [ObservableProperty]
    public partial int? OverrideSlot { get; set; }

    [ObservableProperty]
    public partial string? OverrideSection { get; set; }

    [ObservableProperty]
    public partial FlagReason FlagReason { get; set; }

    public bool IsFlagged => FlagReason != FlagReason.None;

    partial void OnFlagReasonChanged(FlagReason value)
    {
        OnPropertyChanged(nameof(IsFlagged));
    }

    public OcrMatchResult? OcrResult { get; init; }

    public ScanFlagFix? FlagFix { get; set; }
}
