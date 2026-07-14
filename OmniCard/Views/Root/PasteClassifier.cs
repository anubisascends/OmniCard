using System.Text.RegularExpressions;

namespace OmniCard.Views.Root;

// Pure decision logic for Ctrl+V paste-to-assign in the scanned queue.
// Kept separate from RootViewModel so it can be unit-tested without the view-model.
public static partial class PasteClassifier
{
    public enum PasteKind { Empty, Code, Name }

    // Same pattern RootViewModel.ManualSearch uses to detect SET-NUM codes.
    [GeneratedRegex(@"^([A-Za-z0-9]+)-(\d+[A-Za-z]*)$")]
    private static partial Regex CodeRegex();

    internal static PasteKind Classify(string? clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
            return PasteKind.Empty;
        return CodeRegex().IsMatch(clipboardText.Trim()) ? PasteKind.Code : PasteKind.Name;
    }

    // True when a code paste resolved to exactly one DB result and should be assigned
    // directly. Otherwise the caller prefills + focuses the search box for manual picking.
    internal static bool ShouldAssignDirectly(PasteKind kind, int resultCount)
        => kind == PasteKind.Code && resultCount == 1;
}
