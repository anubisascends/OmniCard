using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;

namespace OmniCard.Views.Inventory;

/// <summary>
/// View-only aggregate: a Product plus its owned quantity/cost/market value summed across all lots,
/// and the individual lots shown in the expandable row-details "tree" underneath the product.
/// </summary>
public sealed partial class InventoryRow : ObservableObject
{
    public InventoryRow(Product product, int ownedQuantity, decimal totalCost, decimal totalMarket, IEnumerable<LotRow> lots)
    {
        Product = product;
        OwnedQuantity = ownedQuantity;
        TotalCost = totalCost;
        TotalMarket = totalMarket;
        Lots = new ObservableCollection<LotRow>(lots);
    }

    public Product Product { get; }
    public int OwnedQuantity { get; }
    public decimal TotalCost { get; }
    public decimal TotalMarket { get; }

    /// <summary>The lots backing this product, shown as child rows in the DataGrid row-details.</summary>
    public ObservableCollection<LotRow> Lots { get; }

    /// <summary>Drives the row-details ("tree") expander for this product. Two-way bound to the
    /// chevron ToggleButton and to <c>DataGridRow.DetailsVisibility</c>.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public string Name => Product.Name;
    public CardGame Game => Product.Game;
    public ProductCategory Category => Product.Category;
    public string? SetCode => Product.SetCode;
    public decimal? UnitCost => OwnedQuantity > 0 ? TotalCost / OwnedQuantity : null;
}

/// <summary>
/// View-only projection of a single <see cref="InventoryLot"/> for the product's expanded lot list,
/// carrying the resolved storage-location name for display.
/// </summary>
public sealed record LotRow(InventoryLot Lot, string? LocationName)
{
    public int LotId => Lot.Id;
    public int Quantity => Lot.Quantity;
    public decimal? UnitCost => Lot.UnitCost;
    public decimal TotalCost => Lot.Quantity * (Lot.UnitCost ?? 0m);
    public string? Source => Lot.Source;
    public DateTime AcquisitionDate => Lot.AcquisitionDate;
    public string? Condition => Lot.Condition;
    public string LocationDisplay => LocationName ?? "—";

    /// <summary>Active on-market status of this lot (Listed/Picked), or null when not for sale.
    /// Populated in <see cref="InventoryViewModel.LoadInventory"/> from <see cref="IListingService"/>.
    /// Drives the lot's state pill, mirroring the card tile's LISTED/PICKED/eBAY badge.</summary>
    public ListingStatus? ListingStatus { get; init; }

    /// <summary>The channel the active listing is on (null when not listed). Used to show the
    /// distinct "eBAY" pill for eBay listings.</summary>
    public SalesChannel? ListingChannel { get; init; }

    /// <summary>Pill text: "eBAY" for an eBay listing, else "PICKED"/"LISTED"; "" when not listed.</summary>
    public string ListingBadge => ListingChannel == SalesChannel.Ebay
        ? "eBAY"
        : ListingStatus switch
        {
            Models.ListingStatus.Picked => "PICKED",
            Models.ListingStatus.Listed => "LISTED",
            _ => "",
        };
}
