using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniCard.Views.Sales;

/// <summary>Required-reason modal: Confirm is disabled while the reason field is blank/whitespace.
/// Used to gate edits to a Completed order (a mandatory audit-trail reason).</summary>
public sealed partial class RequireReasonViewModel : ViewModel
{
    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial string Reason { get; set; } = "";

    public bool CanConfirm => !string.IsNullOrWhiteSpace(Reason);

    partial void OnReasonChanged(string value) => OnPropertyChanged(nameof(CanConfirm));

    public Action<bool?>? CloseDialog { get; set; }

    /// <summary>The trimmed reason, set on Confirm. Null if the dialog was cancelled.</summary>
    public string? Result { get; private set; }

    public void Load(string title, string message)
    {
        Title = title;
        Message = message;
        Reason = "";
        Result = null;
    }

    [RelayCommand]
    public void Confirm()
    {
        if (!CanConfirm) return;
        Result = Reason.Trim();
        CloseDialog?.Invoke(true);
    }

    [RelayCommand]
    public void Cancel() => CloseDialog?.Invoke(false);
}
