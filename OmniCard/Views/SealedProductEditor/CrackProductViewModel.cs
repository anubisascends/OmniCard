using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.SealedProductEditor;

public sealed partial class CrackProductViewModel(ISealedProductService sealedProductService, IDialogService dialogService) : ViewModel
{
    private SealedProductInstance _instance = null!;

    [ObservableProperty]
    public partial string ProductName { get; set; } = "";

    [ObservableProperty]
    public partial string ProductInfo { get; set; } = "";

    public ObservableCollection<CrackContentLine> ContentLines { get; } = [];

    public List<SealedProductInstance>? Result { get; private set; }
    public Action<bool>? CloseDialog { get; set; }

    public void Load(SealedProductInstance instance)
    {
        _instance = instance;
        ProductName = instance.Template.Name;
        ProductInfo = $"{instance.Template.ProductType} — {(instance.PurchasePrice.HasValue ? $"${instance.PurchasePrice:F2}" : "No price")}";

        ContentLines.Clear();
        foreach (var content in instance.Template.Contents)
        {
            ContentLines.Add(new CrackContentLine
            {
                ContentId = content.Id,
                Quantity = content.Quantity,
                ChildProductType = content.ChildProductType,
                ChildTemplateName = content.ChildTemplate?.Name,
                ChildTemplateId = content.ChildTemplateId,
                NeedsTemplate = content.ChildTemplateId is null,
            });
        }
    }

    [RelayCommand]
    public void PickTemplate(CrackContentLine line)
    {
        // Let user pick or create a template for this content line
        var template = dialogService.EditSealedProductTemplate(new SealedProductTemplate
        {
            ProductType = line.ChildProductType,
            SetCode = _instance.Template.SetCode,
        });

        if (template is not null)
        {
            line.ChildTemplateId = template.Id;
            line.ChildTemplateName = template.Name;
            line.NeedsTemplate = false;
        }
    }

    [RelayCommand]
    public void Crack()
    {
        var overrides = new Dictionary<int, int>();
        foreach (var line in ContentLines)
        {
            if (line.ChildTemplateId.HasValue && line.ContentId > 0)
                overrides[line.ContentId] = line.ChildTemplateId.Value;
        }

        Result = sealedProductService.CrackInstanceWithTemplates(_instance.Id, overrides);
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}

public partial class CrackContentLine : ObservableObject
{
    public int ContentId { get; init; }
    public int Quantity { get; init; }
    public SealedProductType ChildProductType { get; init; }

    [ObservableProperty]
    public partial string? ChildTemplateName { get; set; }

    [ObservableProperty]
    public partial int? ChildTemplateId { get; set; }

    [ObservableProperty]
    public partial bool NeedsTemplate { get; set; }
}
