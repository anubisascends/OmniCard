using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.Root;

public sealed partial class SealedProductViewModel : ViewModel
{
    private readonly ISealedProductService _sealedProductService;
    private readonly IDialogService _dialogService;

    public SealedProductViewModel(ISealedProductService sealedProductService, IDialogService dialogService)
    {
        _sealedProductService = sealedProductService;
        _dialogService = dialogService;
    }

    [ObservableProperty]
    public partial bool ShowSealed { get; set; }

    public ObservableCollection<SealedProductInstance> Instances { get; } = [];

    [ObservableProperty]
    public partial SealedProductInstance? SelectedInstance { get; set; }

    [ObservableProperty]
    public partial string UpcEntry { get; set; } = "";

    /// <summary>Set by RootViewModel to report status messages.</summary>
    public Action<string>? ReportMessage { get; set; }

    /// <summary>Set by RootView to switch to the scanner tab.</summary>
    public Action? LaunchScanner { get; set; }

    public void LoadInstances()
    {
        Instances.Clear();
        foreach (var instance in _sealedProductService.GetInstances())
            Instances.Add(instance);
    }

    [RelayCommand]
    public void AddByUpc()
    {
        if (string.IsNullOrWhiteSpace(UpcEntry)) return;

        var template = _sealedProductService.FindTemplateByUpc(UpcEntry.Trim());
        if (template is not null)
        {
            _sealedProductService.AddInstance(template.Id, null);
            ReportMessage?.Invoke($"Added {template.Name}.");
            UpcEntry = "";
            LoadInstances();
        }
        else
        {
            // UPC not found — open template editor to create one with the UPC pre-filled
            var newTemplate = _dialogService.EditSealedProductTemplate(new SealedProductTemplate
            {
                Upc = UpcEntry.Trim(),
                ProductType = SealedProductType.BoosterBox,
            });
            if (newTemplate is not null)
            {
                _sealedProductService.AddInstance(newTemplate.Id, null);
                ReportMessage?.Invoke($"Created and added {newTemplate.Name}.");
                UpcEntry = "";
                LoadInstances();
            }
        }
    }

    [RelayCommand]
    public void AddByTemplate()
    {
        var instance = _dialogService.AddSealedProduct();
        if (instance is not null)
        {
            ReportMessage?.Invoke($"Added {instance.Template.Name}.");
            LoadInstances();
        }
    }

    [RelayCommand]
    public void DeleteInstance()
    {
        if (SelectedInstance is null) return;
        _sealedProductService.DeleteInstance(SelectedInstance.Id);
        ReportMessage?.Invoke($"Deleted {SelectedInstance.Template.Name}.");
        LoadInstances();
    }

    [RelayCommand]
    public void CrackInstance()
    {
        if (SelectedInstance is null) return;

        var instance = SelectedInstance;

        // Card-type: just delete and launch scanner
        if (instance.Template.ProductType == SealedProductType.Card)
        {
            _sealedProductService.DeleteInstance(instance.Id);
            LoadInstances();
            LaunchScanner?.Invoke();
            ReportMessage?.Invoke($"Opened {instance.Template.Name} — scan the cards.");
            return;
        }

        // Open crack dialog
        var children = _dialogService.CrackSealedProduct(instance);
        if (children is not null)
        {
            ReportMessage?.Invoke($"Cracked {instance.Template.Name} into {children.Count} items.");
            LoadInstances();
        }
    }

    [RelayCommand]
    public void ManageTemplates()
    {
        _dialogService.EditSealedProductTemplate(null);
    }

    [RelayCommand]
    public void EditInstance()
    {
        if (SelectedInstance is null) return;
        _dialogService.EditSealedProductTemplate(SelectedInstance.Template);
        LoadInstances();
    }
}
