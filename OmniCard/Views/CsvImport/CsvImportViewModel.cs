using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.CsvImport;

public sealed partial class CsvImportViewModel(
    ICsvExportImportService csvService,
    IStorageContainerService containerService) : ViewModel
{
    private CsvImportPreview _preview = null!;

    public ObservableCollection<CollectionCard> PreviewCards { get; } = [];
    public ObservableCollection<StorageContainer> AvailableContainers { get; } = [];

    [ObservableProperty]
    public partial string FormatLabel { get; set; } = "";

    [ObservableProperty]
    public partial string CardCountLabel { get; set; } = "";

    [ObservableProperty]
    public partial string WarningLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool HasWarnings { get; set; }

    [ObservableProperty]
    public partial bool SkipDuplicates { get; set; } = true;

    [ObservableProperty]
    public partial bool CanImport { get; set; }

    [ObservableProperty]
    public partial StorageContainer? SelectedContainer { get; set; }

    public int ImportedCount { get; private set; }

    public Action<bool>? CloseDialog { get; set; }

    public void LoadPreview(CsvImportPreview preview)
    {
        _preview = preview;

        FormatLabel = preview.DetectedFormat switch
        {
            CsvFormat.AppNative => "Detected: App-Native Format",
            CsvFormat.TcgPlayer => "Detected: TCGPlayer Format",
            CsvFormat.Moxfield => "Detected: Moxfield Format",
            CsvFormat.Manabox => "Detected: Manabox / Mythic Tools Format",
            _ => "Unknown Format",
        };

        CardCountLabel = $"{preview.Cards.Count} cards found";
        HasWarnings = preview.Warnings.Count > 0;
        WarningLabel = HasWarnings ? $"{preview.Warnings.Count} rows skipped" : "";
        CanImport = preview.Cards.Count > 0;

        PreviewCards.Clear();
        foreach (var card in preview.Cards.Take(20))
            PreviewCards.Add(card);

        AvailableContainers.Clear();
        foreach (var c in containerService.GetAll())
            AvailableContainers.Add(c);

        // Default to Bulk
        SelectedContainer = AvailableContainers.FirstOrDefault(c => c.IsSystem);
    }

    [RelayCommand]
    public void Import()
    {
        ImportedCount = csvService.ImportCards(_preview, SkipDuplicates, SelectedContainer?.Id);
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel()
    {
        CloseDialog?.Invoke(false);
    }
}
