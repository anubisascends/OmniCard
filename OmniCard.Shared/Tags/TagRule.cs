using OmniCard.Shared.Cards;

namespace OmniCard.Shared.Tags;

/// <summary>
/// An administrator-defined auto-tagging rule: every card of <see cref="Game"/> that matches
/// <see cref="Query"/> (search syntax, validated by the web layer) gets each of <see cref="Tags"/>.
///
/// <para>Rules are <b>additive only</b> — applying one never removes a tag. They are applied to new
/// cards as they are scanned (pre-filled in scan review, where the user can remove them) and imported,
/// and on demand to existing cards ("Run now"). Disabled rules are kept but never applied.</para>
///
/// <para>Tags are stored by name, not by <see cref="Tag"/> id, so a rule can name a tag that doesn't
/// exist yet (it is created the first time the rule tags a card).</para>
/// </summary>
public class TagRule
{
    public const int MaxNameLength = 100;
    public const int MaxQueryLength = 1000;

    public int Id { get; set; }

    public string Name { get; set; } = "";

    public CardGame Game { get; set; }

    public string Query { get; set; } = "";

    /// <summary>The tag names to add, serialized by the web layer (JSON string array).</summary>
    public string TagsJson { get; set; } = "[]";

    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
