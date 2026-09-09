using OmniCard.Shared.Cards;
using OmniCard.Shared.Lists;
namespace OmniCard.Shared.Scanning;

/// <summary>Resolved destination for one game's group of scans in the "Create List from Scans"
/// dialog: either an existing list to append to, or a name for a brand-new one.</summary>
public record ScanListTargetResult(CardGame Game, CardList? ExistingList, bool CreateNew, string NewName);
