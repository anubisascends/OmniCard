using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;

namespace OmniCard.Views.DeckBoxSync;

/// <summary>One card in the deck box the target list no longer wants. The user either keeps it as
/// sideboard (tagged, left in place) or moves the cut copies to a chosen location.</summary>
public sealed partial class DeckBoxCutRowVm : ObservableObject
{
    public DeckBoxCutRowVm(DeckBoxCutRow row, IReadOnlyList<StorageContainer> availableLocations)
    {
        Row = row;
        AvailableLocations = availableLocations;
    }

    public DeckBoxCutRow Row { get; }
    public IReadOnlyList<StorageContainer> AvailableLocations { get; }

    public string CardName => Row.CardName;
    public string? SetCode => Row.SetCode;
    public bool IsFoil => Row.IsFoil;
    public int Quantity => Row.Quantity;
    public string? ImageUri => Row.ImageUri;
    public string Label => Quantity > 1 ? $"{Quantity}× {CardName}" : CardName;

    // Default to Sideboard — the safe, non-destructive resolution (card stays with the deck).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoveToEditable))]
    [NotifyPropertyChangedFor(nameof(HasChoice))]
    public partial bool Sideboard { get; set; } = true;

    // Writable mirror so the "Move to" radio can drive Sideboard (WPF TwoWay needs a settable property).
    public bool MoveToEditable { get => !Sideboard; set => Sideboard = !value; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChoice))]
    public partial StorageContainer? SelectedLocation { get; set; }

    public bool HasChoice => Sideboard || SelectedLocation is not null;

    public DeckBoxCutDecision ToDecision() =>
        new(Row.LotId, Row.Quantity, Sideboard, Sideboard ? null : SelectedLocation?.Id);
}

/// <summary>One card the target list wants that the box lacks. The user picks which owned copy elsewhere
/// in the collection to pull in (or leaves it missing when no source is chosen / available).</summary>
public sealed partial class DeckBoxAddRowVm : ObservableObject
{
    public DeckBoxAddRowVm(DeckBoxAddRow row)
    {
        Row = row;
        Sources = row.Sources;
        // Default to the best (exact-printing, most-available) source so a plain confirm does the obvious thing.
        SelectedSource = Sources.FirstOrDefault();
    }

    public DeckBoxAddRow Row { get; }
    public IReadOnlyList<DeckBoxAddSource> Sources { get; }

    public string CardName => Row.CardName;
    public string? SetCode => Row.SetCode;
    public int QuantityNeeded => Row.QuantityNeeded;
    public string? ImageUri => Row.ImageUri;
    public string Label => QuantityNeeded > 1 ? $"{QuantityNeeded}× {CardName}" : CardName;
    public bool HasSources => Sources.Count > 0;
    public bool NoSources => Sources.Count == 0;
    public string NoSourceHint => HasSources ? "" : "Not found in collection";

    [ObservableProperty] public partial DeckBoxAddSource? SelectedSource { get; set; }

    /// <summary>Skip this add (leave the card missing from the deck) even when a source is available.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WillAdd))]
    [NotifyPropertyChangedFor(nameof(NotSkip))]
    public partial bool Skip { get; set; }

    public bool NotSkip => !Skip;

    public bool WillAdd => !Skip && SelectedSource is not null;

    /// <summary>The decision for this row, or null when nothing will be moved (skipped / no source).</summary>
    public DeckBoxAddDecision? ToDecision()
    {
        if (Skip || SelectedSource is null) return null;
        var qty = Math.Min(QuantityNeeded, SelectedSource.AvailableQty);
        return qty >= 1 ? new DeckBoxAddDecision(SelectedSource.LotId, qty) : null;
    }
}
