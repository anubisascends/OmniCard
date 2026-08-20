using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.EbayListing;

public sealed partial class EbayListingViewModel(
    IEbayCatalogService catalogService,
    IEbayListingService listingService,
    IEbaySellingSettingsService sellingSettings,
    ILogger<EbayListingViewModel> logger) : ViewModel
{
    // Card-single mode state (set by LoadCard).
    private CollectionCard? _card;
    // Sealed-product mode state (set by LoadSealedProduct). In sealed mode the SKU/listing is
    // keyed by the lot id rather than a CollectionCard.
    private Product? _sealedProduct;
    private int _sealedLotId;

    public Action<bool?>? CloseDialog { get; set; }

    /// <summary>True when listing a sealed product rather than a card single. Drives the sealed
    /// branch in <see cref="CreateListing"/> and hides card-only fields in the view.</summary>
    [ObservableProperty] public partial bool IsSealed { get; set; }

    /// <summary>Inverse of <see cref="IsSealed"/>, for binding card-only UI visibility.</summary>
    public bool IsCardSingle => !IsSealed;
    partial void OnIsSealedChanged(bool value) => OnPropertyChanged(nameof(IsCardSingle));

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
        Title = $"{GameTitlePrefix(card.Game)} {card.Name} [{card.SetCode}] #{card.Number} {card.Condition}{foilStr}".TrimStart();
        Description = $"{card.Name} from {card.SetName} ({card.SetCode}) #{card.Number}.\n" +
                      $"Condition: {card.Condition}. {(card.IsFoil ? "Foil finish." : "")}";

        // Kick off catalog search and policy fetch
        _ = SearchCatalogCommand.ExecuteAsync(null);
        _ = LoadPoliciesAsync();
    }

    /// <summary>Loads a sealed inventory product (booster box/pack/case/etc.) for listing. Mirrors
    /// <see cref="LoadCard"/> but keys the listing by the owning lot id and marks the dialog sealed
    /// so the service builds a NEW-condition item and the view hides card-only fields.</summary>
    public void LoadSealedProduct(Product product, int lotId, decimal? suggestedPrice)
    {
        _sealedProduct = product;
        _sealedLotId = lotId;
        IsSealed = true;

        CardName = product.Name;
        SetInfo = product.SetName ?? "";
        SetCode = product.SetCode ?? "";
        CardNumber = "";
        Rarity = "";
        Condition = "New";
        IsFoil = false;
        ApiImageUri = product.ImageUri;
        Price = suggestedPrice ?? product.MarketPrice;

        var setStr = string.IsNullOrWhiteSpace(product.SetCode) ? "" : $" [{product.SetCode}]";
        var prefix = GameTitlePrefix(product.Game);
        Title = $"{prefix} {product.Name}{setStr} {product.Category} Sealed".Trim();
        Description = $"Factory-sealed {product.Name}" +
                      (string.IsNullOrWhiteSpace(product.SetName) ? "" : $" from {product.SetName}") +
                      $" ({product.Category}). Brand new, never opened.";

        // Kick off catalog search (by product name → market price + a real leaf category) and policies.
        _ = SearchCatalogCommand.ExecuteAsync(null);
        _ = LoadPoliciesAsync();
    }

    /// <summary>Short game prefix for the auto-generated eBay listing title (e.g. "MTG").</summary>
    public static string GameTitlePrefix(CardGame game) => game switch
    {
        CardGame.Mtg => "MTG",
        CardGame.Pokemon => "Pokémon",
        CardGame.YuGiOh => "Yu-Gi-Oh!",
        CardGame.OnePiece => "One Piece",
        CardGame.FinalFantasy => "Final Fantasy",
        CardGame.Riftbound => "Riftbound",
        _ => "",
    };

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
        if (!sellingSettings.IsSetupComplete())
        {
            ErrorMessage = "eBay setup incomplete. Open Settings ▸ eBay Selling and click Run eBay Setup first.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var options = BuildOptions();
            var success = IsSealed
                ? await listingService.CreateSealedListingAsync(_sealedProduct!, _sealedLotId, options)
                : await listingService.CreateListingAsync(_card!, options);

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
