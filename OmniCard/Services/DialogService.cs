using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using OmniCard.Interfaces;
using OmniCard.Models;
using OmniCard.Views.Card;
using OmniCard.Views.CollectionCardEditor;
using OmniCard.Views.Connection;
using OmniCard.Views.CoverArtPicker;
using OmniCard.Views.CsvImport;
using OmniCard.Views.DataLocation;
using OmniCard.Views.EbayAuth;
using OmniCard.Views.SetFilterBuilder;
using OmniCard.Views.SortFilterBuilder;
using OmniCard.Views.MoveToLocation;
using OmniCard.Views.SealedProductEditor;
using OmniCard.Views.AuditReport;
using OmniCard.Views.StorageManager;
using OmniCard.Views.EbayListing;

namespace OmniCard.Services;

public sealed class DialogService(IServiceProvider services) : IDialogService
{
    public IServiceProvider Services { get; } = services;

    private CardView? _cardWindow;

    public (bool Connected, bool SetAsDefault) ConnectToScanner()
    {
        var wnd = Services.GetRequiredService<ConnectionView>();
        wnd.Owner = Application.Current.MainWindow;
        var result = wnd.ShowDialog() == true;
        return (result, result && wnd.ViewModel.SetAsDefault);
    }

    public bool? ConnectToEbay()
    {
        var wnd = Services.GetRequiredService<EbayAuthView>();
        wnd.Owner = Application.Current.MainWindow;
        return wnd.ShowDialog();
    }

    public void ShowCard(ScannedCard card)
    {
        if (_cardWindow is null)
        {
            _cardWindow = Services.GetRequiredService<CardView>();
            _cardWindow.Owner = Application.Current.MainWindow;
            _cardWindow.Closed += (_, _) => _cardWindow = null;
        }

        _cardWindow.ViewModel.Card = card;
        _cardWindow.Show();
        _cardWindow.Activate();
    }

    public bool? EditCollectionCard(CollectionCard card)
    {
        var wnd = Services.GetRequiredService<CollectionCardEditorView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ViewModel.LoadCard(card);
        return wnd.ShowDialog();
    }

    public void ManageStorageContainers()
    {
        var wnd = Services.GetRequiredService<StorageManagerView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ShowDialog();
    }

    public int? ShowImportPreview(CsvImportPreview preview)
    {
        var wnd = Services.GetRequiredService<CsvImportView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ViewModel.LoadPreview(preview);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.ImportedCount : null;
    }

    public bool OpenSortFilterBuilder(CardGame game)
    {
        var wnd = Services.GetRequiredService<SortFilterBuilderView>();
        wnd.ViewModel.Initialize(game);
        wnd.Owner = Application.Current.MainWindow;
        wnd.ShowDialog();
        return wnd.ViewModel.PresetsChanged;
    }

    public IReadOnlyList<string>? OpenSetFilterBuilder(IReadOnlyList<SetInfo> allSets, IReadOnlySet<string>? currentFilter)
    {
        var wnd = Services.GetRequiredService<SetFilterBuilderView>();
        wnd.ViewModel.Initialize(allSets, currentFilter);
        wnd.Owner = Application.Current.MainWindow;
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.GetSelectedCodes() : null;
    }

    public void ShowDataLocation()
    {
        var wnd = Services.GetRequiredService<DataLocationView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ShowDialog();
    }

    public int? PickCoverArt(int containerId, string containerName)
    {
        var wnd = Services.GetRequiredService<CoverArtPickerView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ViewModel.Load(containerId, containerName);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.SelectedCardId : null;
    }

    public MoveToLocationResult? PickMoveToLocation()
    {
        var wnd = Services.GetRequiredService<MoveToLocationView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ViewModel.Load();
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }

    public SealedProductTemplate? EditSealedProductTemplate(SealedProductTemplate? existing)
    {
        var wnd = Services.GetRequiredService<SealedProductTemplateEditorView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ViewModel.Load(existing);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }

    public List<SealedProductInstance>? OpenSealedProductEntry()
    {
        var wnd = Services.GetRequiredService<SealedProductEntryView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ViewModel.Load();
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }

    public List<SealedProductInstance>? CrackSealedProduct(SealedProductInstance instance)
    {
        var sealedProductService = Services.GetRequiredService<ISealedProductService>();
        var fullInstance = sealedProductService.GetInstanceWithContents(instance.Id) ?? instance;
        var wnd = Services.GetRequiredService<CrackProductView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ViewModel.Load(fullInstance);
        var result = wnd.ShowDialog();
        return result == true ? wnd.ViewModel.Result : null;
    }

    public void ShowAuditReport(AuditReport report)
    {
        var wnd = Services.GetRequiredService<AuditReportView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ViewModel.Load(report);
        wnd.ShowDialog();
    }

    public bool? OpenEbayListingDialog(CollectionCard card)
    {
        var wnd = Services.GetRequiredService<EbayListingView>();
        wnd.Owner = Application.Current.MainWindow;
        wnd.ViewModel.LoadCard(card);
        return wnd.ShowDialog();
    }
}
