using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniCard.Models;

namespace OmniCard.Views.SetFilterBuilder;

/// <summary>
/// WPF-specific extension of SetFilterItem that adds an observable DrawingImage Symbol property
/// for displaying set symbols in the SetFilterBuilder UI.
/// </summary>
public partial class WpfSetFilterItem : SetFilterItem, INotifyPropertyChanged
{
    private DrawingImage? _symbol;

    public DrawingImage? Symbol
    {
        get => _symbol;
        set
        {
            if (_symbol != value)
            {
                _symbol = value;
                OnPropertyChanged(nameof(Symbol));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
