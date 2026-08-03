using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniCard.Models;

public partial class ScannedCard : ObservableObject
{
    [ObservableProperty]
    public partial string TempImagePath { get; set; } = "";

    public required ulong Hash { get; set; }
    public ulong[]? ArtHashes { get; init; }
    public ulong? ScanEdgeHash { get; set; }

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

    /// <summary>True when the DB has zero copies of this exact (Game, GameCardId, Foil) print —
    /// set by <see cref="OmniCard.Interfaces.ICardService.AnnotateScan"/>, not computed locally.</summary>
    [ObservableProperty]
    public partial bool IsFirstCopy { get; set; }

    /// <summary>Current market price for the matched print, set by
    /// <see cref="OmniCard.Interfaces.ICardService.AnnotateScan"/>.</summary>
    [ObservableProperty]
    public partial decimal? CurrentPrice { get; set; }

    public bool IsFlagged => FlagReason != FlagReason.None;

    partial void OnFlagReasonChanged(FlagReason value)
    {
        OnPropertyChanged(nameof(IsFlagged));
    }

    public OcrMatchResult? OcrResult { get; init; }

    public ScanFlagFix? FlagFix { get; set; }
}
