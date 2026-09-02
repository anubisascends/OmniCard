namespace OmniCard.Models;

/// <summary>Natural ("human") ordering for collector numbers, which mix digits and letters
/// (e.g. "1", "2", "10", "12a", "12b", "TG04", "GN001"). Plain string ordering would put
/// "10" before "2"; this compares maximal digit-runs numerically and letter-runs
/// case-insensitively, so sets sort the way a binder does.</summary>
public sealed class CollectorNumberComparer : IComparer<string>
{
    public static readonly CollectorNumberComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        x ??= "";
        y ??= "";

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            bool dx = char.IsDigit(x[ix]);
            bool dy = char.IsDigit(y[iy]);

            if (dx && dy)
            {
                // Compare two digit-runs numerically (ignore leading zeros, tie-break by length).
                int sx = ix, sy = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;

                var nx = x.AsSpan(sx, ix - sx).TrimStart('0');
                var ny = y.AsSpan(sy, iy - sy).TrimStart('0');
                if (nx.Length != ny.Length) return nx.Length - ny.Length;
                int cmp = nx.SequenceCompareTo(ny);
                if (cmp != 0) return cmp;
            }
            else
            {
                int cmp = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (cmp != 0) return cmp;
                ix++;
                iy++;
            }
        }

        return (x.Length - ix) - (y.Length - iy);
    }
}
