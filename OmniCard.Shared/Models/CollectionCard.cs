using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace OmniCard.Models;

public class CollectionCard : INotifyPropertyChanged
{
    public int Id { get; set; }
    public CardGame Game { get; set; }
    public string GameCardId { get; set; } = "";
    public string Name { get; set; } = "";
    public string SetName { get; set; } = "";
    public string SetCode { get; set; } = "";
    public string Number { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string? ImageUri { get; set; }
    public string? ScanImagePath { get; set; }
    public string Condition { get; set; } = "NM";
    public bool IsFoil { get; set; }
    /// <summary>Foil finish sub-type (e.g. "Etched", "Reverse Holofoil"). Null for non-foil.
    /// Mirrors <see cref="Product.FoilType"/>. See <see cref="FoilTypes"/>.</summary>
    public string? FoilType { get; set; }
    public decimal? PurchasePrice { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;

    public int? ContainerId { get; set; }
    public StorageContainer? Container { get; set; }
    public int? Page { get; set; }
    public int? Slot { get; set; }
    public string? Section { get; set; }
    public string? Color { get; set; }
    public string? CardType { get; set; }

    /// <summary>Full catalog type line (e.g. "Legendary Creature — Human Wizard"), hydrated on
    /// demand for deck-box grouping. Not persisted on the lot/product — looked up from the game
    /// catalog. Null until hydrated (or for games without a type line).</summary>
    [NotMapped]
    public string? TypeLine { get; set; }

    /// <summary>Converted mana value / total mana cost, hydrated on demand for deck-box grouping.
    /// Not persisted — looked up from the game catalog. Null until hydrated (or non-MTG cards).</summary>
    [NotMapped]
    public double? ManaValue { get; set; }

    public bool IsMissing { get; set; }
    public FlagReason? FlagReason { get; set; }
    public EbayListing? EbayListing { get; set; }
    public bool IsTraded { get; set; }
    public string? TradeNote { get; set; }

    /// <summary>User-defined tags on this physical copy. Populated in a separate pass after the
    /// base query, same as <see cref="ListingStatus"/> — there's no scalar tags column to
    /// project directly. Reassigning (not mutating in place) notifies bound tile badges.</summary>
    public List<string> Tags
    {
        get => _tags;
        set
        {
            _tags = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tags)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTags)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TagsDisplay)));
        }
    }
    private List<string> _tags = [];

    [NotMapped]
    public bool HasTags => Tags.Count > 0;

    [NotMapped]
    public string TagsDisplay => string.Join(", ", Tags);

    /// <summary>Cached market price for display and sorting. Not persisted.</summary>
    [NotMapped]
    public decimal MarketPrice
    {
        get => _marketPrice;
        set
        {
            if (_marketPrice == value) return;
            _marketPrice = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MarketPrice)));
        }
    }
    private decimal _marketPrice;

    /// <summary>Display-only quantity when stacking identical cards. Not persisted.</summary>
    [NotMapped]
    public int Quantity { get; set; } = 1;

    /// <summary>Active listing status for this lot (Listed/Picked), or null if not on the market. Not persisted.</summary>
    [NotMapped]
    public ListingStatus? ListingStatus { get; set; }

    /// <summary>All card IDs in this stack (including this card). Only populated when stacked.</summary>
    [NotMapped]
    public List<int>? StackedIds { get; set; }

    /// <summary>Transient UI selection state for the deck-box stacks view (the flat ListBox uses its
    /// own ListBoxItem.IsSelected instead). Not persisted. Reactive so the tile border updates.</summary>
    [NotMapped]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;
}
