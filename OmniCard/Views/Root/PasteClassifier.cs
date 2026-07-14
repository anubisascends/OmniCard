using System.Text.RegularExpressions;

namespace OmniCard.Views.Root;

// Pure decision logic for Ctrl+V paste-to-assign in the scanned queue.
// Kept separate from RootViewModel so it can be unit-tested without the view-model.
internal static partial class PasteClassifier
{
    internal enum PasteKind { Empty, Code, Name }

    // Single source of truth for the SET-NUM code pattern, shared with
    // RootViewModel.SetCollectorNumberRegex().
    internal const string CodePattern = @"^([A-Za-z0-9]+)-(\d+[A-Za-z]*)$";

    [GeneratedRegex(CodePattern)]
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
