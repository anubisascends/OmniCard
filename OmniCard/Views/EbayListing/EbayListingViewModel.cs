using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.EbayListing;

public sealed partial class EbayListingViewModel(
    IEbayCatalogService catalogService,
    IEbayListingService listingService,
    ILogger<EbayListingViewModel> logger) : ViewModel
{
    private CollectionCard _card = null!;

    public Action<bool?>? CloseDialog { get; set; }

    // --- Card info (read-only) ---
    [ObservableProperty] public partial string CardName { get; set; } = "";
    [ObservableProperty] public partial string SetInfo { get; set; } = "";
    [ObservableProperty] public partial string CardNumber { get; set; } = "";
    [ObservableProperty] public partial string Rarity { get; set; } = "";
    [ObservableProperty] public partial string SetCode { get; set; } = "";
    [ObservableProperty] public partial string Condition { get; set; } = "";
    [ObservableProperty] public partial bool IsFoil { get; set; }
    [ObservableProperty] public partial decimal? PurchasePrice { get; set; }
    [ObservableProperty] public partial string? ScanImagePath { get; set; }
    [ObservableProperty] public partial string? ApiImageUri { get; set; }

    // --- Listing configuration ---
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Description { get; set; } = "";
    [ObservableProperty] public partial EbayListingType ListingType { get; set; } = EbayListingType.FixedPrice;
    [ObservableProperty] public partial decimal Price { get; set; }
    [ObservableProperty] public partial int AuctionDuration { get; set; } = 7;
    [ObservableProperty] public partial bool IncludeScanImage { get; set; } = true;
    [ObservableProperty] public partial bool IncludeStockImage { get; set; } = true;
    [ObservableProperty] public partial string? EbayCategoryId { get; set; }

    public bool IsAuction => ListingType == EbayListingType.Auction;
    partial void OnListingTypeChanged(EbayListingType value) => OnPropertyChanged(nameof(IsAuction));

    // --- Catalog / Market ---
    public ObservableCollection<EbayCatalogMatch> CatalogMatches { get; } = [];
    [ObservableProperty] public partial EbayCatalogMatch? SelectedCatalogMatch { get; set; }
    [ObservableProperty] public partial EbayMarketPrice? MarketPrice { get; set; }
    [ObservableProperty] public partial bool IsSearchingCatalog { get; set; }

    // --- Seller policies ---
    public ObservableCollection<EbaySellerPolicy> ShippingPolicies { get; } = [];
    public ObservableCollection<EbaySellerPolicy> ReturnPolicies { get; } = [];
    public ObservableCollection<EbaySellerPolicy> PaymentPolicies { get; } = [];
    [ObservableProperty] public partial EbaySellerPolicy? SelectedShippingPolicy { get; set; }
    [ObservableProperty] public partial EbaySellerPolicy? SelectedReturnPolicy { get; set; }
    [ObservableProperty] public partial EbaySellerPolicy? SelectedPaymentPolicy { get; set; }

    // --- State ---
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string? ErrorMessage { get; set; }

    public decimal? EstimatedProfit => MarketPrice is not null && PurchasePrice.HasValue
        ? MarketPrice.MedianPrice - PurchasePrice.Value
        : null;

    public void LoadCard(CollectionCard card)
    {
        _card = card;
        CardName = card.Name;
        SetInfo = card.SetName;
        SetCode = card.SetCode;
        CardNumber = card.Number;
        Rarity = card.Rarity;
        Condition = card.Condition;
        IsFoil = card.IsFoil;
        PurchasePrice = card.PurchasePrice;
        ScanImagePath = card.ScanImagePath;
        ApiImageUri = card.ImageUri;

        // Auto-generate title and description
        var foilStr = card.IsFoil ? " FOIL" : "";
        Title = $"MTG {card.Name} [{card.SetCode}] #{card.Number} {card.Condition}{foilStr}";
        Description = $"{card.Name} from {card.SetName} ({card.SetCode}) #{card.Number}.\n" +
                      $"Condition: {card.Condition}. {(card.IsFoil ? "Foil finish." : "")}";

        // Kick off catalog search and policy fetch
        _ = SearchCatalogCommand.ExecuteAsync(null);
        _ = LoadPoliciesAsync();
    }

    [RelayCommand]
    public async Task SearchCatalog()
    {
        IsSearchingCatalog = true;
        ErrorMessage = null;

        try
        {
            CatalogMatches.Clear();
            var results = await catalogService.SearchCatalogAsync(CardName, SetInfo, CardNumber);
            foreach (var match in results)
                CatalogMatches.Add(match);

            if (CatalogMatches.Count > 0)
            {
                SelectedCatalogMatch = CatalogMatches[0];
                EbayCategoryId = SelectedCatalogMatch.CategoryId;
            }

            // Fetch market price
            var marketPrice = await catalogService.GetMarketPriceAsync(
                $"{CardName} {SetInfo} {Condition}", Condition, IsFoil);

            MarketPrice = marketPrice;
            OnPropertyChanged(nameof(EstimatedProfit));

            if (marketPrice is not null && Price == 0)
                Price = marketPrice.MedianPrice;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Catalog search failed");
            ErrorMessage = "Failed to search eBay catalog.";
        }
        finally
        {
            IsSearchingCatalog = false;
        }
    }

    private async Task LoadPoliciesAsync()
    {
        try
        {
            var shipping = await listingService.GetSellerPoliciesAsync("fulfillment");
            var returns = await listingService.GetSellerPoliciesAsync("return");
            var payment = await listingService.GetSellerPoliciesAsync("payment");

            ShippingPolicies.Clear();
            foreach (var p in shipping) ShippingPolicies.Add(p);
            if (ShippingPolicies.Count > 0) SelectedShippingPolicy = ShippingPolicies[0];

            ReturnPolicies.Clear();
            foreach (var p in returns) ReturnPolicies.Add(p);
            if (ReturnPolicies.Count > 0) SelectedReturnPolicy = ReturnPolicies[0];

            PaymentPolicies.Clear();
            foreach (var p in payment) PaymentPolicies.Add(p);
            if (PaymentPolicies.Count > 0) SelectedPaymentPolicy = PaymentPolicies[0];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load seller policies");
        }
    }

    [RelayCommand]
    public async Task CreateListing()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var options = BuildOptions();
            var success = await listingService.CreateListingAsync(_card, options);

            if (success)
            {
                CloseDialog?.Invoke(true);
            }
            else
            {
                ErrorMessage = "Failed to create eBay listing. Check logs for details.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create listing");
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);

    private EbayListingOptions BuildOptions() => new()
    {
        ListingType = ListingType,
        Price = Price,
        AuctionDuration = IsAuction ? AuctionDuration : null,
        Condition = Condition,
        Title = Title,
        Description = Description,
        IncludeScanImage = IncludeScanImage,
        IncludeStockImage = IncludeStockImage,
        ShippingPolicyId = SelectedShippingPolicy?.PolicyId,
        ReturnPolicyId = SelectedReturnPolicy?.PolicyId,
        PaymentPolicyId = SelectedPaymentPolicy?.PolicyId,
        EbayCategoryId = EbayCategoryId,
    };
}
