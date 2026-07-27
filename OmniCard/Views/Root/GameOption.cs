using OmniCard.Models;

namespace OmniCard.Views.Root;

/// <summary>
/// A ComboBox-friendly wrapper for a game choice. <see cref="Game"/> is null for the
/// "All Games" option. A non-null wrapper is required because WPF's <c>Selector</c> treats a
/// null <c>SelectedItem</c> as "no selection", so a bare null item cannot hold selection —
/// which is why selecting "All Games" previously did not stick.
/// </summary>
public sealed class GameOption
{
    public CardGame? Game { get; init; }
}
