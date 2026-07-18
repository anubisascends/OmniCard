using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Views.CollectionCardEditor;

public sealed partial class CollectionCardEditorViewModel : ViewModel
{
    private readonly ICardService _cardService;
    private readonly IStorageContainerService _containerService;
    private readonly IPerceptualHashService _hashService;
    private readonly IDataPathService _dataPathService;
    private CollectionCard _originalCard = null!;
    private List<int> _stackedIds = [];

    public CollectionCardEditorViewModel(ICardService cardService, IStorageContainerService containerService, IPerceptualHashService hashService, IDataPathService dataPathService)
    {
        _cardService = cardService;
        _containerService = containerService;
        _hashService = hashService;
        _dataPathService = dataPathService;
    }

    // Card identity (read-only display)
    [ObservableProperty]
    public partial string CardName { get; set; } = "";

    [ObservableProperty]
    public partial string SetInfo { get; set; } = "";

    [ObservableProperty]
    public partial string SetCode { get; set; } = "";

    [ObservableProperty]
    public partial string CardNumber { get; set; } = "";

    [ObservableProperty]
    public partial string Rarity { get; set; } = "";

    [ObservableProperty]
    public partial string GameName { get; set; } = "";

    // Editable fields
    [ObservableProperty]
    public partial string Condition { get; set; } = "NM";

    [ObservableProperty]
    public partial bool IsFoil { get; set; }

    [ObservableProperty]
    public partial decimal? PurchasePrice { get; set; }

    // Images
    [ObservableProperty]
    public partial BitmapImage? ScanImage { get; set; }

    public bool HasScanImage => ScanImage is not null;

    partial void OnScanImageChanged(BitmapImage? value) => OnPropertyChanged(nameof(HasScanImage));

    [ObservableProperty]
    public partial BitmapImage? ApiImage { get; set; }

    // Stack scan image flipper
    private List<(int CardId, string? ScanPath)> _stackedScans = [];

    [ObservableProperty]
    public partial int ScanImageIndex { get; set; }

    public int ScanImageCount => _stackedScans.Count;
    public string ScanImageLabel => _stackedScans.Count > 0
        ? $"{ScanImageIndex + 1} of {_stackedScans.Count}"
        : "Scan";
    public bool IsStacked => _stackedIds.Count > 1;
    public bool CanPreviousScan => ScanImageIndex > 0;
    public bool CanNextScan => ScanImageIndex < _stackedScans.Count - 1;

    [ObservableProperty]
    public partial bool ApplyToAll { get; set; } = true;

    [RelayCommand]
    public void PreviousScan()
    {
        if (ScanImageIndex > 0)
            ScanImageIndex--;
    }

    [RelayCommand]
    public void NextScan()
    {
        if (ScanImageIndex < _stackedScans.Count - 1)
            ScanImageIndex++;
    }

    partial void OnScanImageIndexChanged(int value)
    {
        LoadScanImageAt(value);
        OnPropertyChanged(nameof(ScanImageLabel));
        OnPropertyChanged(nameof(CanPreviousScan));
        OnPropertyChanged(nameof(CanNextScan));
    }

    private void LoadScanImageAt(int index)
    {
        ScanImage = null;
        if (index < 0 || index >= _stackedScans.Count) return;

        var scanPath = _stackedScans[index].ScanPath;
        if (scanPath is null) return;

        var fullPath = Path.Combine(_dataPathService.DataDirectory, scanPath);
        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0) return;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(fullPath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            ScanImage = bmp;
        }
        catch (NotSupportedException) { }
    }

    // Location
    public ObservableCollection<StorageContainer> AvailableContainers { get; } = [];

    [ObservableProperty]
    public partial StorageContainer? SelectedContainer { get; set; }

    [ObservableProperty]
    public partial int? Page { get; set; }

    [ObservableProperty]
    public partial int? Slot { get; set; }

    [ObservableProperty]
    public partial string? Section { get; set; }

    public bool ShowBinderFields => SelectedContainer?.ContainerType == ContainerType.Binder;
    public bool ShowBoxFields => SelectedContainer?.ContainerType == ContainerType.Box;

    partial void OnSelectedContainerChanged(StorageContainer? value)
    {
        // Clear position fields when switching container type
        if (value?.ContainerType != ContainerType.Binder)
        {
            Page = null;
            Slot = null;
        }
        if (value?.ContainerType != ContainerType.Box)
        {
            Section = null;
        }
        OnPropertyChanged(nameof(ShowBinderFields));
        OnPropertyChanged(nameof(ShowBoxFields));
    }

    // Printing selector
    public ObservableCollection<CardMatch> AvailablePrintings { get; } = [];

    [ObservableProperty]
    public partial CardMatch? SelectedPrinting { get; set; }

    public bool ShowPrintingSelector => AvailablePrintings.Count > 0;

    partial void OnSelectedPrintingChanged(CardMatch? value)
    {
        if (value is null) return;
        if (value.GameSpecificId == _originalCard.GameCardId) return;

        _originalCard.GameCardId = value.GameSpecificId;
        _originalCard.SetName = value.SetName;
        _originalCard.SetCode = value.SetCode;
        _originalCard.Number = value.CollectorNumber;
        _originalCard.Rarity = value.Rarity;
        _originalCard.ImageUri = value.ImageUri;

        SetInfo = $"{value.SetName} ({value.SetCode})";
        SetCode = value.SetCode;
        CardNumber = value.CollectorNumber;
        Rarity = value.Rarity;

        if (value.ImageUri is not null)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(value.ImageUri, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 300;
            bmp.EndInit();
            ApiImage = bmp;
        }

        // Record correction if scan image exists
        if (_originalCard.ScanImagePath is not null)
        {
            try
            {
                var imagePath = Path.Combine(_dataPathService.DataDirectory, _originalCard.ScanImagePath);
                if (File.Exists(imagePath))
                {
                    using var stream = File.OpenRead(imagePath);
                    var hash = _hashService.ComputeHash(stream);
                    _cardService.GetGameService(_originalCard.Game).RecordCorrection(hash, value.GameSpecificId);
                }
            }
            catch { }
        }
    }

    private void RefreshPrintings()
    {
        AvailablePrintings.Clear();
        try
        {
            var printings = _cardService.GetGameService(_originalCard.Game).GetPrintings(_originalCard.Name);
            foreach (var p in printings)
                AvailablePrintings.Add(p);
            SelectedPrinting = AvailablePrintings.FirstOrDefault(p => p.GameSpecificId == _originalCard.GameCardId);
        }
        catch { }
        OnPropertyChanged(nameof(ShowPrintingSelector));
    }

    // Reassignment search
    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSearchVisible { get; set; }

    public bool IsSearchHidden => !IsSearchVisible;
    public bool HasSearchResults => SearchResults.Count > 0;

    public ObservableCollection<CardMatch> SearchResults { get; } = [];

    [ObservableProperty]
    public partial CardMatch? SelectedSearchResult { get; set; }

    [RelayCommand]
    public void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        OnPropertyChanged(nameof(IsSearchHidden));
    }

    // eBay listing info (read-only display)
    [ObservableProperty] public partial EbayListingStatus? EbayStatus { get; set; }
    [ObservableProperty] public partial string? EbayItemId { get; set; }
    [ObservableProperty] public partial decimal? EbayListedPrice { get; set; }
    [ObservableProperty] public partial decimal? EbaySoldPrice { get; set; }
    [ObservableProperty] public partial string? EbayBuyerUsername { get; set; }

    public bool HasEbayListing => EbayStatus.HasValue;
    public bool IsEbayActive => EbayStatus == EbayListingStatus.Active;
    public bool IsEbaySold => EbayStatus == EbayListingStatus.Sold;
    public bool CanListOnEbay => !IsEbayActive;

    public string EbayStatusDisplay => EbayStatus?.ToString() ?? "Not Listed";

    // Dialog result
    public bool WasSaved { get; private set; }
    public bool WasDeleted { get; private set; }

    // Dialog close callback
    public Action<bool?>? CloseDialog { get; set; }

    public void LoadCard(CollectionCard card)
    {
        _originalCard = card;
        _stackedIds = card.StackedIds ?? [card.Id];

        CardName = card.Name;
        SetInfo = $"{card.SetName} ({card.SetCode})";
        SetCode = card.SetCode;
        CardNumber = card.Number;
        Rarity = card.Rarity;
        GameName = card.Game.ToString();
        Condition = card.Condition;
        IsFoil = card.IsFoil;
        PurchasePrice = card.PurchasePrice;

        // Load scan images for stack flipper
        if (_stackedIds.Count > 1)
        {
            var siblings = _cardService.GetCollectionCards(_stackedIds);
            _stackedScans = siblings.Select(c => (c.Id, c.ScanImagePath)).ToList();
        }
        else
        {
            _stackedScans = [(card.Id, card.ScanImagePath)];
        }
        OnPropertyChanged(nameof(ScanImageCount));
        OnPropertyChanged(nameof(IsStacked));
        ScanImageIndex = 0;
        LoadScanImageAt(0);
        OnPropertyChanged(nameof(ScanImageLabel));
        OnPropertyChanged(nameof(CanPreviousScan));
        OnPropertyChanged(nameof(CanNextScan));

        // Load API card art from URL — look up from game DB if not stored on the collection card
        var imageUri = card.ImageUri ?? ResolveImageUri(card.Game, card.GameCardId);
        if (imageUri is not null)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(imageUri, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            ApiImage = bmp;
        }

        // Load location
        AvailableContainers.Clear();
        foreach (var c in _containerService.GetAll())
            AvailableContainers.Add(c);

        SelectedContainer = AvailableContainers.FirstOrDefault(c => c.Id == card.ContainerId);
        Page = card.Page;
        Slot = card.Slot;
        Section = card.Section;

        RefreshPrintings();

        // eBay listing info
        EbayStatus = card.EbayListing?.Status;
        EbayItemId = card.EbayListing?.EbayItemId;
        EbayListedPrice = card.EbayListing?.ListedPrice;
        EbaySoldPrice = card.EbayListing?.SoldPrice;
        EbayBuyerUsername = card.EbayListing?.BuyerUsername;
        OnPropertyChanged(nameof(HasEbayListing));
        OnPropertyChanged(nameof(IsEbayActive));
        OnPropertyChanged(nameof(IsEbaySold));
        OnPropertyChanged(nameof(CanListOnEbay));
        OnPropertyChanged(nameof(EbayStatusDisplay));
    }

    [RelayCommand]
    public void Search()
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            OnPropertyChanged(nameof(HasSearchResults));
            return;
        }

        var service = _cardService.GetGameService(_originalCard.Game);
        foreach (var match in service.SearchCards(SearchQuery))
            SearchResults.Add(match);
        OnPropertyChanged(nameof(HasSearchResults));
    }

    [RelayCommand]
    public void AssignCard()
    {
        if (SelectedSearchResult is null)
            return;

        var match = SelectedSearchResult;
        _originalCard.GameCardId = match.GameSpecificId;
        _originalCard.Name = match.Name;
        _originalCard.SetName = match.SetName;
        _originalCard.SetCode = match.SetCode;
        _originalCard.Number = match.CollectorNumber;
        _originalCard.Rarity = match.Rarity;
        _originalCard.ImageUri = match.ImageUri;

        // Update display
        CardName = match.Name;
        SetInfo = $"{match.SetName} ({match.SetCode})";
        SetCode = match.SetCode;
        CardNumber = match.CollectorNumber;
        Rarity = match.Rarity;

        // Reload API image (no Freeze — WPF downloads asynchronously)
        if (match.ImageUri is not null)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(match.ImageUri, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            ApiImage = bmp;
        }

        // Record correction from scan image if available
        if (_originalCard.ScanImagePath is not null)
        {
            try
            {
                var imagePath = Path.Combine(_dataPathService.DataDirectory, _originalCard.ScanImagePath);

                if (File.Exists(imagePath))
                {
                    using var stream = File.OpenRead(imagePath);
                    var hash = _hashService.ComputeHash(stream);
                    _cardService.GetGameService(_originalCard.Game).RecordCorrection(hash, match.GameSpecificId);
                }
            }
            catch
            {
                // Silently skip correction if scan image can't be read
            }
        }

        SearchResults.Clear();
        SearchQuery = "";
        SelectedSearchResult = null;
        IsSearchVisible = false;
        OnPropertyChanged(nameof(IsSearchHidden));
        OnPropertyChanged(nameof(HasSearchResults));

        RefreshPrintings();
    }

    [RelayCommand]
    public void Save()
    {
        // Update the representative card
        _originalCard.Condition = Condition;
        _originalCard.IsFoil = IsFoil;
        _originalCard.PurchasePrice = PurchasePrice;
        _originalCard.ContainerId = SelectedContainer?.Id;
        _originalCard.Page = SelectedContainer?.ContainerType == ContainerType.Binder ? Page : null;
        _originalCard.Slot = SelectedContainer?.ContainerType == ContainerType.Binder ? Slot : null;
        _originalCard.Section = SelectedContainer?.ContainerType == ContainerType.Box ? Section : null;
        _cardService.UpdateCollectionCard(_originalCard);

        // Apply to all stacked siblings when checked
        if (ApplyToAll)
        {
            var siblingIds = _stackedIds.Where(id => id != _originalCard.Id).ToList();
            if (siblingIds.Count > 0)
            {
                var containerId = SelectedContainer?.Id;
                var page = SelectedContainer?.ContainerType == ContainerType.Binder ? Page : null;
                var slot = SelectedContainer?.ContainerType == ContainerType.Binder ? Slot : null;
                var section = SelectedContainer?.ContainerType == ContainerType.Box ? Section : null;

                _cardService.BulkUpdateField(siblingIds, c =>
                {
                    c.GameCardId = _originalCard.GameCardId;
                    c.Name = _originalCard.Name;
                    c.SetName = _originalCard.SetName;
                    c.SetCode = _originalCard.SetCode;
                    c.Number = _originalCard.Number;
                    c.Rarity = _originalCard.Rarity;
                    c.ImageUri = _originalCard.ImageUri;
                    c.Condition = Condition;
                    c.IsFoil = IsFoil;
                    c.PurchasePrice = PurchasePrice;
                    c.ContainerId = containerId;
                    c.Page = page;
                    c.Slot = slot;
                    c.Section = section;
                });
            }
        }

        WasSaved = true;
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void MoveToBulk()
    {
        SelectedContainer = AvailableContainers.FirstOrDefault(c => c.IsSystem);
    }

    [RelayCommand]
    public void Delete()
    {
        var idsToDelete = ApplyToAll ? _stackedIds : [_originalCard.Id];
        var count = idsToDelete.Count;
        var message = count > 1
            ? $"Are you sure you want to delete all {count} copies of this card from your collection?"
            : "Are you sure you want to delete this card from your collection?";

        var result = System.Windows.MessageBox.Show(
            message, "Delete Card", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        foreach (var id in idsToDelete)
            _cardService.DeleteCollectionCard(id);
        WasDeleted = true;
        CloseDialog?.Invoke(false);
    }

    private string? ResolveImageUri(CardGame game, string gameCardId)
    {
        try
        {
            return CardImageUriResolver.From(_cardService.GetGameService(game).FindCardById(gameCardId));
        }
        catch
        {
            return null;
        }
    }
}
