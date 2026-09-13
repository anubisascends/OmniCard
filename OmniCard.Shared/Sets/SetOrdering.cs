using System.Globalization;

namespace OmniCard.Shared.Sets;

// Shared ordering for set lists. Historically every game ordered sets
// lexicographically by display name, which sorts "One Piece 10" before
// "One Piece 2" and scatters numbered sets. We instead order by a natural
// (numeric-aware) comparison of the set CODE, so codes group by their alpha
// prefix and then ascend numerically ("OP02" < "OP10" < "OP17"), with the
// display name as a stable tie-break.
public static class SetOrdering
{
    public static IReadOnlyList<SetInfo> InNaturalOrder(this IEnumerable<SetInfo> sets)
        => sets
            .OrderBy(s => s.SetCode, NaturalSetCodeComparer.Instance)
            .ThenBy(s => s.SetName, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

// Compares strings so embedded numbers sort by value rather than by character.
// Alpha runs compare case-insensitively; digit runs compare numerically
// (leading zeros ignored), so "OP2" < "OP10" and "ST01" < "ST1a" behave
// sensibly. Nulls sort first.
public sealed class NaturalSetCodeComparer : IComparer<string>
{
    public static readonly NaturalSetCodeComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            bool xDigit = char.IsDigit(x[ix]);
            bool yDigit = char.IsDigit(y[iy]);

            if (xDigit && yDigit)
            {
                // Compare complete digit runs numerically.
                int sx = ix, sy = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;

                var nx = x.AsSpan(sx, ix - sx).TrimStart('0');
                var ny = y.AsSpan(sy, iy - sy).TrimStart('0');

                if (nx.Length != ny.Length)
                    return nx.Length - ny.Length;
                int cmp = nx.CompareTo(ny, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
                // Equal value; shorter original (fewer leading zeros) sorts first.
                if ((ix - sx) != (iy - sy))
                    return (ix - sx) - (iy - sy);
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
