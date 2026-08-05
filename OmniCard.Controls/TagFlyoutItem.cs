using System.ComponentModel;

namespace OmniCard.Controls;

/// <summary>One row of a <see cref="TagFlyout"/>'s checklist: a tag name plus whether it is
/// present on all, none, or some of the current selection.</summary>
public sealed class TagFlyoutItem(string name, TagCheckState state) : INotifyPropertyChanged
{
    public string Name { get; } = name;

    private TagCheckState _state = state;
    public TagCheckState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIndeterminate)));
        }
    }

    public bool IsChecked => State == TagCheckState.Checked;
    public bool IsIndeterminate => State == TagCheckState.Indeterminate;

    public event PropertyChangedEventHandler? PropertyChanged;
}
