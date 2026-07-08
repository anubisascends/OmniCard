using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Services;
using OmniCard.Collection;

namespace OmniCard.Views.SealedProductEditor;

public sealed partial class SealedProductEntryViewModel(
    ISealedProductService sealedProductService,
    IEnumerable<ICardGameService> gameServices) : ViewModel
{
    private List<SetInfo> _sets = [];

    // UPC entry
    [ObservableProperty]
    public partial string UpcEntry { get; set; } = "";

    // New product fields (shown when UPC not found or manual add)
    [ObservableProperty]
    public partial bool ShowNewProductFields { get; set; }

    [ObservableProperty]
    public partial bool IsManualAdd { get; set; }

    [ObservableProperty]
    public partial SealedProductType SelectedProductType { get; set; } = SealedProductType.PlayBoosterBox;

    [ObservableProperty]
    public partial string SetEntry { get; set; } = "";

    [ObservableProperty]
    public partial string? MatchedSetName { get; set; }

    [ObservableProperty]
    public partial string? MatchedSetCode { get; set; }

    [ObservableProperty]
    public partial string GeneratedName { get; set; } = "";

    // Price
    [ObservableProperty]
    public partial string PriceEntry { get; set; } = "";

    // Known template (set when UPC matches)
    [ObservableProperty]
    public partial SealedProductTemplate? MatchedTemplate { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "";

    // Session items
    public ObservableCollection<SessionItem> SessionItems { get; } = [];

    // All product types for the dropdown (uses ProductTypeDisplayConverter for display)
    public IReadOnlyList<SealedProductType> AllProductTypes { get; } =
        Enum.GetValues<SealedProductType>().ToList();

    // Set suggestions for autocomplete
    public ObservableCollection<SetInfo> SetSuggestions { get; } = [];

    public List<SealedProductInstance> Result { get; } = [];
    public Action<bool>? CloseDialog { get; set; }
    public Action? FocusUpcField { get; set; }
    public Action? FocusPriceField { get; set; }

    public void Load()
    {
        // Load sets from all game services (primarily MTG)
        _sets = gameServices
            .SelectMany(s => s.GetAvailableSets())
            .DistinctBy(s => s.SetCode)
            .OrderBy(s => s.SetName)
            .ToList();

        // AllProductTypes is already initialized via property initializer
    }

    partial void OnSetEntryChanged(string value)
    {
        UpdateSetSuggestions(value);
        TryMatchSet(value);
        UpdateGeneratedName();
    }

    partial void OnSelectedProductTypeChanged(SealedProductType value)
    {
        UpdateGeneratedName();
    }

    private void UpdateSetSuggestions(string text)
    {
        SetSuggestions.Clear();
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2) return;

        var matches = _sets
            .Where(s => s.SetName.Contains(text, StringComparison.OrdinalIgnoreCase)
                     || s.SetCode.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Take(10);

        foreach (var s in matches)
            SetSuggestions.Add(s);
    }

    private void TryMatchSet(string text)
    {
        var match = _sets.FirstOrDefault(s =>
            s.SetCode.Equals(text, StringComparison.OrdinalIgnoreCase)
            || s.SetName.Equals(text, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            MatchedSetCode = match.SetCode;
            MatchedSetName = match.SetName;
        }
        else
        {
            MatchedSetCode = null;
            MatchedSetName = null;
        }
    }

    private void UpdateGeneratedName()
    {
        GeneratedName = SealedProductArchetypeRegistry.GenerateTemplateName(
            SelectedProductType, MatchedSetName ?? (string.IsNullOrWhiteSpace(SetEntry) ? null : SetEntry));
    }

    public void SelectSet(SetInfo set)
    {
        MatchedSetCode = set.SetCode;
        MatchedSetName = set.SetName;
        SetEntry = set.SetName;
        SetSuggestions.Clear();
        UpdateGeneratedName();
    }

    [RelayCommand]
    public void LookupUpc()
    {
        if (string.IsNullOrWhiteSpace(UpcEntry)) return;

        var template = sealedProductService.FindTemplateByUpc(UpcEntry.Trim());
        if (template is not null)
        {
            MatchedTemplate = template;
            ShowNewProductFields = false;
            StatusMessage = $"Found: {template.Name}";
            FocusPriceField?.Invoke();
        }
        else
        {
            MatchedTemplate = null;
            ShowNewProductFields = true;
            IsManualAdd = false;
            StatusMessage = "UPC not found — define the product below.";
        }
    }

    [RelayCommand]
    public void ManualAdd()
    {
        MatchedTemplate = null;
        ShowNewProductFields = true;
        IsManualAdd = true;
        UpcEntry = "";
        StatusMessage = "Manual entry — pick type and set.";
    }

    [RelayCommand]
    public void AddProduct()
    {
        if (MatchedTemplate is null && !ShowNewProductFields) return;

        decimal? price = decimal.TryParse(PriceEntry, out var parsed) ? parsed : null;

        SealedProductTemplate template;
        if (MatchedTemplate is not null)
        {
            template = MatchedTemplate;
        }
        else
        {
            var upc = IsManualAdd ? null : UpcEntry.Trim();
            template = sealedProductService.CreateTemplateFromArchetype(
                SelectedProductType,
                MatchedSetCode ?? (string.IsNullOrWhiteSpace(SetEntry) ? null : SetEntry.Trim()),
                MatchedSetName,
                string.IsNullOrWhiteSpace(upc) ? null : upc);
        }

        var instance = sealedProductService.AddInstance(template.Id, price);
        Result.Add(instance);

        SessionItems.Insert(0, new SessionItem(
            instance.Id,
            template.Name,
            price,
            SealedProductArchetypeRegistry.GetDisplayName(template.ProductType)));

        // Reset for next entry
        StatusMessage = $"Added: {template.Name}";
        UpcEntry = "";
        PriceEntry = "";
        SetEntry = "";
        MatchedTemplate = null;
        ShowNewProductFields = false;
        IsManualAdd = false;
        MatchedSetCode = null;
        MatchedSetName = null;
        GeneratedName = "";

        FocusUpcField?.Invoke();
    }

    [RelayCommand]
    public void RemoveSessionItem(SessionItem item)
    {
        sealedProductService.DeleteInstance(item.InstanceId);
        Result.RemoveAll(i => i.Id == item.InstanceId);
        SessionItems.Remove(item);
        StatusMessage = $"Removed: {item.Name}";
    }

    [RelayCommand]
    public void Done() => CloseDialog?.Invoke(true);
}

public record SessionItem(int InstanceId, string Name, decimal? Price, string TypeDisplay);

public class ProductTypeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SealedProductType type ? SealedProductArchetypeRegistry.GetDisplayName(type) : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
