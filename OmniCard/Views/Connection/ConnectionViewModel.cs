using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Windows;
using OmniCard.Services;

namespace OmniCard.Views.Connection;

public sealed partial class ConnectionViewModel(ScannerService scannerService, ILogger<ConnectionViewModel> logger) : ViewModel
{
    private readonly ILogger<ConnectionViewModel> _logger = logger;

    public ScannerService ScannerService { get; } = scannerService;

    [ObservableProperty]
    public partial bool SetAsDefault { get; set; }

    [RelayCommand]
    public void Ok()
    {
        _logger.LogInformation("Scanner selected: {DataSource} (setAsDefault={Default})", ScannerService.DataSource?.Name ?? "(none)", SetAsDefault);

        var view = Application
             .Current
             .Windows
             .OfType<IView>()
             .FirstOrDefault(x => x.ViewModel == this);

        if(view is Window wnd)
        {
            wnd.DialogResult = true;
            wnd.Close();
        }
    }

    [RelayCommand]
    public void DoubleClick()
    {
        Ok();
    }
}
