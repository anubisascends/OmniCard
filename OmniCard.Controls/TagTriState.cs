namespace OmniCard.Controls;

/// <summary>Single source of the checked/unchecked/indeterminate rule shared by every
/// <see cref="TagFlyout"/> host (Collection, Locations, Scanner): checked when every item in the
/// selection has the tag, unchecked when none do, indeterminate otherwise.</summary>
public static class TagTriState
{
    public static TagCheckState Compute(int countWithTag, int totalCount) => countWithTag switch
    {
        0 => TagCheckState.Unchecked,
        var n when n == totalCount => TagCheckState.Checked,
        _ => TagCheckState.Indeterminate,
    };
}
