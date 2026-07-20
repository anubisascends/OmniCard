using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Models;

namespace OmniCard.Views.Inventory;

public sealed partial class ProductEditorViewModel : ViewModel
{
    private int _productId;

    public IReadOnlyList<CardGame> AllGames { get; } = Enum.GetValues<CardGame>();

    // Phase 1 is sealed-only; ProductCategory.Single is reserved for the Phase 2 singles migration
    // and must not be selectable here.
    public IReadOnlyList<ProductCategory> AllCategories { get; } =
        Enum.GetValues<ProductCategory>().Where(c => c != ProductCategory.Single).ToArray();

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
        var existingCategory = existing?.Category;
        Category = existingCategory is null or ProductCategory.Single ? ProductCategory.Box : existingCategory.Value;
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

        // Defensive guard: Phase 1 is sealed-only. Single is excluded from AllCategories so this
        // should be unreachable via the UI, but never let a Single-category product be saved.
        if (Category == ProductCategory.Single)
        {
            ValidationMessage = "Category 'Single' is reserved for Phase 2 and cannot be used here.";
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
