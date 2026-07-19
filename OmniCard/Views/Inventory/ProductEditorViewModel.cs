using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Models;

namespace OmniCard.Views.Inventory;

public sealed partial class ProductEditorViewModel : ViewModel
{
    private int _productId;

    public IReadOnlyList<CardGame> AllGames { get; } = Enum.GetValues<CardGame>();
    public IReadOnlyList<ProductCategory> AllCategories { get; } = Enum.GetValues<ProductCategory>();

    [ObservableProperty]
    public partial CardGame Game { get; set; }

    [ObservableProperty]
    public partial ProductCategory Category { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string? SetCode { get; set; }

    [ObservableProperty]
    public partial string? Upc { get; set; }

    [ObservableProperty]
    public partial decimal? MarketPrice { get; set; }

    [ObservableProperty]
    public partial string? ImageUri { get; set; }

    [ObservableProperty]
    public partial string? ValidationMessage { get; set; }

    public string Title => _productId == 0 ? "Add Product" : "Edit Product";

    public Action<bool?>? CloseDialog { get; set; }

    public Product? Result { get; private set; }

    public void Load(Product? existing)
    {
        _productId = existing?.Id ?? 0;
        Game = existing?.Game ?? CardGame.Mtg;
        Category = existing?.Category ?? ProductCategory.Single;
        Name = existing?.Name ?? "";
        SetCode = existing?.SetCode;
        Upc = existing?.Upc;
        MarketPrice = existing?.MarketPrice;
        ImageUri = existing?.ImageUri;
        ValidationMessage = null;
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    public void Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationMessage = "Name is required.";
            return;
        }

        Result = new Product
        {
            Id = _productId,
            Game = Game,
            Category = Category,
            Name = Name.Trim(),
            SetCode = string.IsNullOrWhiteSpace(SetCode) ? null : SetCode.Trim(),
            Upc = string.IsNullOrWhiteSpace(Upc) ? null : Upc.Trim(),
            MarketPrice = MarketPrice ?? 0m,
            ImageUri = string.IsNullOrWhiteSpace(ImageUri) ? null : ImageUri.Trim(),
        };
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
