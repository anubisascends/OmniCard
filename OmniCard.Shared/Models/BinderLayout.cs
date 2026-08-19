namespace OmniCard.Models;

public class BinderLayout
{
    public int SlotsPerPage { get; set; }
    public int TotalPages { get; set; }
    public int Columns { get; set; }

    /// <summary>Usable side count (1 or 2) of each physical sheet, in reading order. Lets the
    /// binder UI reason about sheets (for add / remove / insert / move) rather than just the flat
    /// page count. Always sums to <see cref="TotalPages"/>.</summary>
    public IReadOnlyList<int> SheetSides { get; set; } = [];
}
