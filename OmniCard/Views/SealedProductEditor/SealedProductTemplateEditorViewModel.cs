using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Models;
using OmniCard.Services;

namespace OmniCard.Views.SealedProductEditor;

public sealed partial class SealedProductTemplateEditorViewModel(ISealedProductService sealedProductService) : ViewModel
{
    private int? _editingId;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string? SetCode { get; set; }

    [ObservableProperty]
    public partial string? Upc { get; set; }

    [ObservableProperty]
    public partial SealedProductType ProductType { get; set; } = SealedProductType.BoosterBox;

    public ObservableCollection<ContentLineItem> ContentLines { get; } = [];

    public SealedProductTemplate? Result { get; private set; }
    public Action<bool>? CloseDialog { get; set; }

    public void Load(SealedProductTemplate? existing)
    {
        ContentLines.Clear();
        if (existing is not null)
        {
            _editingId = existing.Id;
            Name = existing.Name;
            SetCode = existing.SetCode;
            Upc = existing.Upc;
            ProductType = existing.ProductType;
            foreach (var c in existing.Contents)
            {
                ContentLines.Add(new ContentLineItem
                {
                    Quantity = c.Quantity,
                    ChildProductType = c.ChildProductType,
                    ChildTemplateId = c.ChildTemplateId,
                    ChildTemplateName = c.ChildTemplate?.Name,
                });
            }
        }
    }

    [RelayCommand]
    public void AddContentLine()
    {
        ContentLines.Add(new ContentLineItem { Quantity = 1, ChildProductType = SealedProductType.BoosterPack });
    }

    [RelayCommand]
    public void RemoveContentLine(ContentLineItem line)
    {
        ContentLines.Remove(line);
    }

    [RelayCommand]
    public void PickChildTemplate(ContentLineItem line)
    {
        // Load available templates for the child product type
        var templates = sealedProductService.GetTemplates()
            .Where(t => t.ProductType == line.ChildProductType)
            .ToList();

        if (templates.Count == 0) return;

        // For now, just assign the first match — Plan 2 will add a picker dialog
        // This is a placeholder that works for the service layer
        line.ChildTemplateId = templates[0].Id;
        line.ChildTemplateName = templates[0].Name;
    }

    [RelayCommand]
    public void ClearChildTemplate(ContentLineItem line)
    {
        line.ChildTemplateId = null;
        line.ChildTemplateName = null;
    }

    [RelayCommand]
    public void Save()
    {
        if (string.IsNullOrWhiteSpace(Name)) return;

        var template = new SealedProductTemplate
        {
            Id = _editingId ?? 0,
            Name = Name,
            SetCode = string.IsNullOrWhiteSpace(SetCode) ? null : SetCode.Trim(),
            Upc = string.IsNullOrWhiteSpace(Upc) ? null : Upc.Trim(),
            ProductType = ProductType,
            Contents = ContentLines.Select(c => new SealedProductContents
            {
                Quantity = c.Quantity,
                ChildProductType = c.ChildProductType,
                ChildTemplateId = c.ChildTemplateId,
            }).ToList(),
        };

        if (_editingId.HasValue)
            sealedProductService.UpdateTemplate(template);
        else
            sealedProductService.CreateTemplate(template);

        Result = template;
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}

public partial class ContentLineItem : ObservableObject
{
    [ObservableProperty]
    public partial int Quantity { get; set; } = 1;

    [ObservableProperty]
    public partial SealedProductType ChildProductType { get; set; }

    [ObservableProperty]
    public partial int? ChildTemplateId { get; set; }

    [ObservableProperty]
    public partial string? ChildTemplateName { get; set; }
}
