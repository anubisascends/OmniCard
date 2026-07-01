namespace OmniCard.Services;

/// <summary>
/// Parses Scryfall-style search syntax into field:value pairs.
/// Supported prefixes: name/n, set/s/e, cn/number, type/t, oracle/o, rarity/r, color/c,
/// foil, condition/cond, price, date, location/loc.
/// Plain text defaults to name search. Quoted values supported.
/// </summary>
public static class ScryfallQueryParser
{
    public static List<(string Field, string Value)> Parse(string query)
    {
        var filters = new List<(string Field, string Value)>();
        if (string.IsNullOrWhiteSpace(query))
            return filters;

        var span = query.AsSpan().Trim();
        int i = 0;

        while (i < span.Length)
        {
            while (i < span.Length && char.IsWhiteSpace(span[i])) i++;
            if (i >= span.Length) break;

            int colonIdx = -1;
            for (int j = i; j < span.Length && !char.IsWhiteSpace(span[j]); j++)
            {
                if (span[j] == ':') { colonIdx = j; break; }
            }

            if (colonIdx > i)
            {
                var rawField = span[i..colonIdx].ToString().ToLowerInvariant();
                var field = NormalizeField(rawField);
                i = colonIdx + 1;

                var value = ExtractValue(span, ref i);
                if (value.Length > 0)
                    filters.Add((field, value));
            }
            else
            {
                var value = ExtractValue(span, ref i);
                if (value.Length > 0)
                    filters.Add(("name", value));
            }
        }

        return filters;
    }

    private static string ExtractValue(ReadOnlySpan<char> span, ref int i)
    {
        if (i >= span.Length) return "";

        if (span[i] == '"')
        {
            i++;
            int start = i;
            while (i < span.Length && span[i] != '"') i++;
            var value = span[start..i].ToString();
            if (i < span.Length) i++;
            return value;
        }

        int wordStart = i;
        while (i < span.Length && !char.IsWhiteSpace(span[i])) i++;
        return span[wordStart..i].ToString();
    }

    internal static string NormalizeField(string field) => field switch
    {
        "n" or "name" => "name",
        "s" or "e" or "set" => "set",
        "cn" or "number" => "cn",
        "t" or "type" => "type",
        "o" or "oracle" => "oracle",
        "r" or "rarity" => "rarity",
        "c" or "color" => "color",
        "foil" => "foil",
        "cond" or "condition" => "condition",
        "price" => "price",
        "date" => "date",
        "loc" or "location" => "location",
        _ => field,
    };

    public static string ExpandColor(string c) => c.ToUpperInvariant() switch
    {
        "W" or "WHITE" => "W",
        "U" or "BLUE" => "U",
        "B" or "BLACK" => "B",
        "R" or "RED" => "R",
        "G" or "GREEN" => "G",
        _ => c.ToUpperInvariant(),
    };
}
