namespace OmniCard.Shared.Storage;

/// <summary>A location can't take a name because another location already uses it for the same game (no game
/// counts as its own) in the same site — see <c>StorageContainerService.NameKey</c>. Derives from
/// <see cref="InvalidOperationException"/> so callers that only catch that keep working; API endpoints map it
/// to 409.</summary>
public sealed class LocationNameTakenException(string message) : InvalidOperationException(message);
