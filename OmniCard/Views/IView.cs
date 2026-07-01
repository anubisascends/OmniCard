using System.ComponentModel;
using System.Net.Http.Headers;

namespace OmniCard.Views;

public interface IView
{
    IViewModel ViewModel { get; }
}

public interface IView<TViewModel> : IView
{
    new TViewModel ViewModel { get; }
}
