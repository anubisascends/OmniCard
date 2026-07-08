namespace OmniCard.CardMatching;

public enum ComparisonOp { Contains, Exact, NotEqual, LessThan, GreaterThan, LessOrEqual, GreaterOrEqual }

public abstract record FilterNode;
public record FieldFilter(string Field, ComparisonOp Op, string Value, bool Negated) : FilterNode;
public record AndFilter(List<FilterNode> Children) : FilterNode;
public record OrFilter(List<FilterNode> Children) : FilterNode;
public record NotFilter(FilterNode Inner) : FilterNode;

/// <summary>
/// Parses Scryfall-style search syntax into a filter expression tree.
/// Supports: field:value, field=value, field!=value, field&lt;value, field&gt;value,
/// field&lt;=value, field&gt;=value, OR boolean operator, parentheses grouping,
/// - negation prefix, !name exact match, is:/not: keywords, and quoted values.
/// </summary>
public static class ScryfallQueryParser
{
    public static FilterNode? ParseFilter(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;
        var q = query.Trim();
        int pos = 0;
        return ParseOrExpr(q, ref pos);
    }

    /// <summary>Backward-compatible flat parse (loses operator/negation info).</summary>
    public static List<(string Field, string Value)> Parse(string query)
    {
        var node = ParseFilter(query);
        if (node is null) return [];
        var result = new List<(string Field, string Value)>();
        Flatten(node, result);
        return result;
    }

    private static void Flatten(FilterNode node, List<(string Field, string Value)> result)
    {
        switch (node)
        {
            case FieldFilter f:
                result.Add((f.Field, f.Value));
                break;
            case AndFilter a:
                foreach (var c in a.Children) Flatten(c, result);
                break;
            case OrFilter o:
                foreach (var c in o.Children) Flatten(c, result);
                break;
            case NotFilter n:
                Flatten(n.Inner, result);
                break;
        }
    }

    // or_expr := and_expr ('or' and_expr)*
    private static FilterNode ParseOrExpr(string q, ref int pos)
    {
        var left = ParseAndExpr(q, ref pos);
        var children = new List<FilterNode> { left };

        while (TryConsumeOr(q, ref pos))
            children.Add(ParseAndExpr(q, ref pos));

        return children.Count == 1 ? children[0] : new OrFilter(children);
    }

    // and_expr := atom+
    private static FilterNode ParseAndExpr(string q, ref int pos)
    {
        var children = new List<FilterNode>();

        while (pos < q.Length)
        {
            SkipWhitespace(q, ref pos);
            if (pos >= q.Length || q[pos] == ')') break;
            if (children.Count > 0 && IsOrKeyword(q, pos)) break;
            children.Add(ParseAtom(q, ref pos));
        }

        return children.Count == 1 ? children[0] : new AndFilter(children);
    }

    // atom := '-'? '(' or_expr ')' | '-'? '!' value | '-'? field_filter | '-'? plain_word
    private static FilterNode ParseAtom(string q, ref int pos)
    {
        bool negated = false;
        if (pos < q.Length && q[pos] == '-')
        {
            negated = true;
            pos++;
        }

        // Parenthesized group
        if (pos < q.Length && q[pos] == '(')
        {
            pos++;
            var inner = ParseOrExpr(q, ref pos);
            SkipWhitespace(q, ref pos);
            if (pos < q.Length && q[pos] == ')')
                pos++;
            return negated ? new NotFilter(inner) : inner;
        }

        // Exact name: !value
        if (pos < q.Length && q[pos] == '!')
        {
            pos++;
            var value = ExtractValue(q, ref pos);
            return new FieldFilter("name", ComparisonOp.Exact, value, negated);
        }

        // Try field<operator>value
        int fieldEnd = FindOperatorStart(q, pos);
        if (fieldEnd > pos)
        {
            var rawField = q[pos..fieldEnd].ToLowerInvariant();
            var (op, opLen) = ParseOperator(q, fieldEnd);
            pos = fieldEnd + opLen;
            var value = ExtractValue(q, ref pos);
            var field = NormalizeField(rawField);

            // not: is syntactic sugar for negated is:
            if (field == "not")
                return new FieldFilter("is", op, value, !negated);

            return new FieldFilter(field, op, value, negated);
        }

        // Plain word → name contains
        var word = ExtractValue(q, ref pos);
        return new FieldFilter("name", ComparisonOp.Contains, word, negated);
    }

    private static int FindOperatorStart(string q, int start)
    {
        int i = start;
        while (i < q.Length && (char.IsLetterOrDigit(q[i]) || q[i] == '_'))
            i++;

        if (i == start || i >= q.Length) return -1;

        char c = q[i];
        if (c == ':' || c == '=' || c == '<' || c == '>')
            return i;
        if (c == '!' && i + 1 < q.Length && q[i + 1] == '=')
            return i;

        return -1;
    }

    private static (ComparisonOp Op, int Length) ParseOperator(string q, int pos)
    {
        if (pos >= q.Length) return (ComparisonOp.Contains, 0);

        char c = q[pos];
        char next = pos + 1 < q.Length ? q[pos + 1] : '\0';

        return (c, next) switch
        {
            (':', _) => (ComparisonOp.Contains, 1),
            ('=', _) => (ComparisonOp.Exact, 1),
            ('!', '=') => (ComparisonOp.NotEqual, 2),
            ('<', '=') => (ComparisonOp.LessOrEqual, 2),
            ('>', '=') => (ComparisonOp.GreaterOrEqual, 2),
            ('<', _) => (ComparisonOp.LessThan, 1),
            ('>', _) => (ComparisonOp.GreaterThan, 1),
            _ => (ComparisonOp.Contains, 1),
        };
    }

    private static string ExtractValue(string q, ref int pos)
    {
        if (pos >= q.Length) return "";

        if (q[pos] == '"')
        {
            pos++;
            int start = pos;
            while (pos < q.Length && q[pos] != '"') pos++;
            var value = q[start..pos];
            if (pos < q.Length) pos++;
            return value;
        }

        int wordStart = pos;
        while (pos < q.Length && !char.IsWhiteSpace(q[pos]) && q[pos] != ')' && q[pos] != '(')
            pos++;
        return q[wordStart..pos];
    }

    private static void SkipWhitespace(string q, ref int pos)
    {
        while (pos < q.Length && char.IsWhiteSpace(q[pos])) pos++;
    }

    private static bool IsOrKeyword(string q, int pos)
    {
        if (pos + 2 > q.Length) return false;
        if ((q[pos] != 'o' && q[pos] != 'O') || (q[pos + 1] != 'r' && q[pos + 1] != 'R'))
            return false;
        if (pos + 2 < q.Length && !char.IsWhiteSpace(q[pos + 2]) && q[pos + 2] != '(')
            return false;
        return true;
    }

    private static bool TryConsumeOr(string q, ref int pos)
    {
        int saved = pos;
        SkipWhitespace(q, ref pos);
        if (!IsOrKeyword(q, pos))
        {
            pos = saved;
            return false;
        }

        // Don't consume trailing "or" with nothing after it
        int peek = pos + 2;
        while (peek < q.Length && char.IsWhiteSpace(q[peek])) peek++;
        if (peek >= q.Length || q[peek] == ')')
        {
            pos = saved;
            return false;
        }

        pos += 2;
        return true;
    }

    internal static string NormalizeField(string field) => field switch
    {
        "n" or "name" => "name",
        "s" or "e" or "set" or "edition" => "set",
        "cn" or "number" => "cn",
        "t" or "type" => "type",
        "o" or "oracle" => "oracle",
        "r" or "rarity" => "rarity",
        "c" or "color" => "color",
        "id" or "identity" or "ci" or "commander" => "color",
        "is" => "is",
        "not" => "not",
        "foil" => "foil",
        "cond" or "condition" => "condition",
        "price" => "price",
        "date" => "date",
        "loc" or "location" => "location",
        _ => field,
    };

    private const string WubrgOrder = "WUBRG";

    /// <summary>
    /// Normalizes a color value to WUBRG-ordered characters.
    /// </summary>
    public static string NormalizeColorValue(string value)
    {
        if (value.Equals("colorless", StringComparison.OrdinalIgnoreCase))
            return "";

        var upper = value.ToUpperInvariant();
        return upper switch
        {
            "WHITE" => "W",
            "BLUE" => "U",
            "BLACK" => "B",
            "RED" => "R",
            "GREEN" => "G",
            _ => new string(upper
                .Where(c => WubrgOrder.Contains(c))
                .Distinct()
                .OrderBy(c => WubrgOrder.IndexOf(c))
                .ToArray()),
        };
    }

    public static string ExpandColor(string c) => c.ToUpperInvariant() switch
    {
        "W" or "WHITE" => "W",
        "U" or "BLUE" => "U",
        "B" or "BLACK" => "B",
        "R" or "RED" => "R",
        "G" or "GREEN" => "G",
        _ => c.ToUpperInvariant(),
    };

    internal static readonly string[] RarityOrder = ["common", "uncommon", "rare", "mythic"];

    internal static int RarityRank(string rarity)
    {
        return Array.IndexOf(RarityOrder, rarity.ToLowerInvariant());
    }

    internal static List<string> RaritiesMatching(ComparisonOp op, string value)
    {
        var rank = RarityRank(value);
        if (rank < 0) return [value];

        return op switch
        {
            ComparisonOp.Contains or ComparisonOp.Exact => [RarityOrder[rank]],
            ComparisonOp.NotEqual => RarityOrder.Where((_, i) => i != rank).ToList(),
            ComparisonOp.LessThan => RarityOrder.Where((_, i) => i < rank).ToList(),
            ComparisonOp.GreaterThan => RarityOrder.Where((_, i) => i > rank).ToList(),
            ComparisonOp.LessOrEqual => RarityOrder.Where((_, i) => i <= rank).ToList(),
            ComparisonOp.GreaterOrEqual => RarityOrder.Where((_, i) => i >= rank).ToList(),
            _ => [RarityOrder[rank]],
        };
    }
}
