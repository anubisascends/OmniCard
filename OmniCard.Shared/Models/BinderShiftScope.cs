namespace OmniCard.Models;

/// <summary>Which pages a per-page binder card shift affects, relative to the page the user acted on.
/// Combined with a signed page delta (direction + count) by
/// <see cref="Interfaces.IStorageContainerService.ShiftPage"/>.</summary>
public enum BinderShiftScope
{
    /// <summary>Only the cards on the acted-on page move.</summary>
    OnlyThisPage,

    /// <summary>The acted-on page and every page before it (lower page numbers) move together.</summary>
    ThisAndBefore,

    /// <summary>The acted-on page and every page after it (higher page numbers) move together.</summary>
    ThisAndAfter,
}
