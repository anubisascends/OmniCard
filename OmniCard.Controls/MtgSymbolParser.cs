using System.Text.RegularExpressions;

namespace OmniCard.Controls;

public abstract record MtgTextSegment;
public record TextSegment(string Text) : MtgTextSegment;
public record SymbolSegment(string Code, string FileName) : MtgTextSegment;

public static partial class MtgSymbolParser
{
    private static readonly Dictionary<string, string> SpecialFileNames = new()
    {
        ["½"] = "HALF",
        ["∞"] = "INFINITY",
    };

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex SymbolPattern();

    public static IReadOnlyList<MtgTextSegment> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var segments = new List<MtgTextSegment>();
        var lastIndex = 0;

        foreach (var match in SymbolPattern().EnumerateMatches(text))
        {
            if (match.Index > lastIndex)
                segments.Add(new TextSegment(text[lastIndex..match.Index]));

            var code = text.Substring(match.Index + 1, match.Length - 2);
            segments.Add(new SymbolSegment(code, ResolveFileName(code)));
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            segments.Add(new TextSegment(text[lastIndex..]));

        return segments;
    }

    internal static string ResolveFileName(string code)
    {
        if (SpecialFileNames.TryGetValue(code, out var special))
            return special;

        return code.Replace("/", "");
    }
}
