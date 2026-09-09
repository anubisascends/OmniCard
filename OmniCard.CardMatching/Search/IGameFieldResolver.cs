namespace OmniCard.CardMatching.Search;

/// <summary>
/// Opt-in capability a game service implements to expose its per-game searchable fields. Kept in
/// <c>OmniCard.CardMatching</c> (not on <c>ICardGameService</c> in OmniCard.Shared) because it traffics
/// in <see cref="ComparisonOp"/>/<see cref="SearchSchema"/>, which live here — and OmniCard.Shared
/// must not depend on  Consumers resolve a game service and cast to this.
/// </summary>
public interface IGameFieldResolver
{
    /// <summary>This game's field vocabulary (core fields + its own), driving parser resolution,
    /// catalog search, and the field-metadata API.</summary>
    SearchSchema SearchSchema { get; }

    /// <summary>
    /// Resolve the set of catalog <c>GameCardId</c>s whose cards satisfy a single game-specific
    /// field predicate. Used by collection search to cross the owned-store ↔ catalog DB boundary
    /// (there is no SQL join). The <paramref name="value"/> is raw as typed; implementations apply
    /// their own value aliases via <see cref="SearchSchema.ResolveValue"/>.
    /// <para>Contract: return <c>null</c> when <paramref name="field"/> is not a game-specific field
    /// this game defines (caller falls back to the default name search); an empty set when the field
    /// is recognized but nothing matches; otherwise the matching ids. Semantics for every operator
    /// are a <b>positive match among cards that HAVE the field</b> — cards missing the attribute are
    /// excluded (reachable only via explicit <c>-field:x</c> negation), mirroring <c>tag:</c>.</para>
    /// </summary>
    IReadOnlySet<string>? ResolveFieldCardIds(string field, ComparisonOp op, string value);
}
