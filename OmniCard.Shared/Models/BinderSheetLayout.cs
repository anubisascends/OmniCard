namespace OmniCard.Models;

/// <summary>
/// The ordered physical sheets ("leaves") of a binder. Each sheet has one or two usable sides:
/// a double-sided sheet contributes a front and a back logical page, a single-sided sheet only a
/// front (used for single-pocket pages, or when the user simply doesn't want to use the back).
///
/// Logical page numbers flow in reading order across every sheet — sheet 0's front is page 1, its
/// back is page 2, sheet 1's front is page 3, and so on — which is exactly the flat sequence the
/// spread view already pages through, so single-sided sheets need no special rendering: they just
/// contribute one fewer page. This side-list is the source of truth for a binder's pagination;
/// <see cref="StorageContainer.TotalPages"/> is kept in sync as its sum for the web companion and
/// every other page-based read.
///
/// Add / remove / insert / move all reduce to mutating this list and remapping the affected lots'
/// page numbers — see the transform methods, each of which returns the new layout together with an
/// old-page → new-page map (a null target means "removed; return the card to the Unplaced pool").
/// </summary>
public sealed class BinderSheetLayout
{
    private readonly List<int> _sides;

    private BinderSheetLayout(List<int> sides) => _sides = sides;

    /// <summary>Side count (1 or 2) of each sheet, in reading order.</summary>
    public IReadOnlyList<int> Sides => _sides;

    public int SheetCount => _sides.Count;

    /// <summary>Total logical pages — the sum of every sheet's sides.</summary>
    public int TotalPages => _sides.Sum();

    /// <summary>Parses the persisted CSV side-list (e.g. <c>"2,2,1"</c>). When absent — a legacy
    /// binder predating the sheet model — reconstructs it from <paramref name="totalPages"/> by
    /// grouping the existing logical pages into double-sided sheets (a trailing odd page becomes a
    /// single-sided sheet). That grouping preserves every existing card's page number exactly, so
    /// the backfill is transparent; the first mutation persists the derived list.</summary>
    public static BinderSheetLayout Parse(string? sheetSides, int totalPages)
    {
        if (!string.IsNullOrWhiteSpace(sheetSides))
        {
            var parsed = sheetSides
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var n) && n is 1 or 2 ? n : 2)
                .ToList();
            if (parsed.Count > 0) return new BinderSheetLayout(parsed);
        }

        var sides = new List<int>();
        var remaining = Math.Max(totalPages, 0);
        while (remaining >= 2) { sides.Add(2); remaining -= 2; }
        if (remaining == 1) sides.Add(1);
        if (sides.Count == 0) sides.Add(2); // a binder always has at least one (double-sided) sheet
        return new BinderSheetLayout(sides);
    }

    /// <summary>A brand-new binder: one double-sided sheet (front + back).</summary>
    public static BinderSheetLayout NewDefault() => new([2]);

    public string Serialize() => string.Join(",", _sides);

    /// <summary>1-based logical page number of the front of the given sheet.</summary>
    public int FirstPageOfSheet(int sheetIndex)
    {
        if (sheetIndex < 0 || sheetIndex >= _sides.Count)
            throw new ArgumentOutOfRangeException(nameof(sheetIndex));
        var page = 1;
        for (var i = 0; i < sheetIndex; i++) page += _sides[i];
        return page;
    }

    public int SidesOfSheet(int sheetIndex) => _sides[sheetIndex];

    /// <summary>The 0-based sheet index that owns the given 1-based logical page, or -1 if the
    /// page is out of range.</summary>
    public int SheetIndexOfPage(int page)
    {
        var start = 1;
        for (var i = 0; i < _sides.Count; i++)
        {
            if (page >= start && page < start + _sides[i]) return i;
            start += _sides[i];
        }
        return -1;
    }

    /// <summary>Appends a new sheet to the end of the binder. No existing page moves, so the remap
    /// is empty.</summary>
    public BinderSheetLayout Append(bool doubleSided)
    {
        var sides = new List<int>(_sides) { doubleSided ? 2 : 1 };
        return new BinderSheetLayout(sides);
    }

    /// <summary>Removes a sheet (its one or two logical pages). Returns the new layout plus an
    /// old-page → new-page map: the removed sheet's pages map to <c>null</c> (their cards return to
    /// the Unplaced pool), and every trailing page shifts down by the removed sheet's side count.
    /// Pages before the removed sheet are unchanged and omitted from the map.</summary>
    public (BinderSheetLayout Layout, IReadOnlyDictionary<int, int?> PageRemap) RemoveSheet(int sheetIndex)
    {
        if (sheetIndex < 0 || sheetIndex >= _sides.Count)
            throw new ArgumentOutOfRangeException(nameof(sheetIndex));

        var firstPage = FirstPageOfSheet(sheetIndex);
        var removedSides = _sides[sheetIndex];
        var totalPages = TotalPages;

        var remap = new Dictionary<int, int?>();
        for (var p = firstPage; p < firstPage + removedSides; p++)
            remap[p] = null; // cards on the removed sheet return to Unplaced
        for (var p = firstPage + removedSides; p <= totalPages; p++)
            remap[p] = p - removedSides; // trailing pages shift down to close the gap

        var sides = new List<int>(_sides);
        sides.RemoveAt(sheetIndex);
        return (new BinderSheetLayout(sides), remap);
    }

    /// <summary>Inserts a new (empty) sheet at <paramref name="insertIndex"/> — 0 puts it before the
    /// first sheet, <see cref="SheetCount"/> appends it at the end. Returns the new layout plus an
    /// old-page → new-page map: every existing page at or after the insertion point shifts up by the
    /// new sheet's side count (1 or 2). Earlier pages, and the new sheet's own pages, need no
    /// mapping.</summary>
    public (BinderSheetLayout Layout, IReadOnlyDictionary<int, int?> PageRemap) InsertSheet(int insertIndex, bool doubleSided)
    {
        if (insertIndex < 0 || insertIndex > _sides.Count)
            throw new ArgumentOutOfRangeException(nameof(insertIndex));

        var newSides = doubleSided ? 2 : 1;
        var insertAtPage = insertIndex < _sides.Count ? FirstPageOfSheet(insertIndex) : TotalPages + 1;

        var remap = new Dictionary<int, int?>();
        for (var p = TotalPages; p >= insertAtPage; p--)
            remap[p] = p + newSides; // pages at/after the insertion make room for the new sheet

        var sides = new List<int>(_sides);
        sides.Insert(insertIndex, newSides);
        return (new BinderSheetLayout(sides), remap);
    }

    /// <summary>Pulls the sheet at <paramref name="fromIndex"/> out and reinserts it at
    /// <paramref name="toIndex"/> — an insertion index into the list of the <em>other</em> sheets
    /// (0 = before the first remaining sheet, sheet-count − 1 = at the end). Returns the new layout
    /// plus an old-page → new-page map covering every page whose number changes. A sheet's side
    /// count is preserved, so within-sheet offsets (front stays front, back stays back) carry over;
    /// cards keep their slot and only their page number moves. No pages are removed, so there are no
    /// null targets.</summary>
    public (BinderSheetLayout Layout, IReadOnlyDictionary<int, int?> PageRemap) MoveSheet(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _sides.Count)
            throw new ArgumentOutOfRangeException(nameof(fromIndex));
        if (toIndex < 0 || toIndex >= _sides.Count)
            throw new ArgumentOutOfRangeException(nameof(toIndex));

        // First page of each sheet before the move.
        var oldFirst = new int[_sides.Count];
        var acc = 1;
        for (var i = 0; i < _sides.Count; i++) { oldFirst[i] = acc; acc += _sides[i]; }

        // New order of (old) sheet indices: remove the moved sheet, reinsert at toIndex.
        var order = Enumerable.Range(0, _sides.Count).ToList();
        order.RemoveAt(fromIndex);
        order.Insert(toIndex, fromIndex);

        // First page of each sheet after the move, keyed by its old index.
        var newFirst = new int[_sides.Count];
        var acc2 = 1;
        foreach (var oldIdx in order) { newFirst[oldIdx] = acc2; acc2 += _sides[oldIdx]; }

        var remap = new Dictionary<int, int?>();
        for (var oldIdx = 0; oldIdx < _sides.Count; oldIdx++)
        {
            for (var off = 0; off < _sides[oldIdx]; off++)
            {
                var oldPage = oldFirst[oldIdx] + off;
                var newPage = newFirst[oldIdx] + off;
                if (oldPage != newPage) remap[oldPage] = newPage;
            }
        }

        var newSides = order.Select(i => _sides[i]).ToList();
        return (new BinderSheetLayout(newSides), remap);
    }
}
