using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.SealedProductEditor;

public sealed partial class AddSealedProductViewModel(ISealedProductService sealedProductService) : ViewModel
{
    public ObservableCollection<SealedProductTemplate> Templates { get; } = [];

    [ObservableProperty]
    public partial SealedProductTemplate? SelectedTemplate { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial decimal? PurchasePrice { get; set; }

    private List<SealedProductTemplate> _allTemplates = [];

    public SealedProductInstance? Result { get; private set; }
    public Action<bool>? CloseDialog { get; set; }

    public void Load()
    {
        _allTemplates = sealedProductService.GetTemplates();
        FilterTemplates();
    }

    partial void OnSearchTextChanged(string value) => FilterTemplates();

    private void FilterTemplates()
    {
        Templates.Clear();
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allTemplates
            : _allTemplates.Where(t => t.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || (t.SetCode is not null && t.SetCode.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                || (t.Upc is not null && t.Upc.Contains(SearchText, StringComparison.OrdinalIgnoreCase))).ToList();

        foreach (var t in filtered)
            Templates.Add(t);
    }

    [RelayCommand]
    public void Add()
    {
        if (SelectedTemplate is null) return;
        Result = sealedProductService.AddInstance(SelectedTemplate.Id, PurchasePrice);
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
