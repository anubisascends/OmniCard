namespace OmniCard.CardMatching;

/// <summary>
/// A game's searchable-field vocabulary. Resolves raw field tokens to canonical names (game aliases
/// win, then the shared MTG-agnostic defaults, then identity) and applies per-field value aliases.
/// Built via <see cref="SharedSearchSchema.WithGameFields"/> so every game schema already contains
/// the core aliases; there is no global mutable state, so the classic <c>e:</c>→set vs <c>e:</c>→element
/// collision is resolved per game (whichever schema is passed to the parser wins).
/// </summary>
public sealed class SearchSchema
{
    private readonly Dictionary<string, SearchFieldDefinition> _byAlias;
    private readonly Dictionary<string, SearchFieldDefinition> _byCanonical;

    public IReadOnlyList<SearchFieldDefinition> Fields { get; }

    public SearchSchema(IEnumerable<SearchFieldDefinition> fields)
    {
        Fields = fields.ToList();
        _byAlias = new Dictionary<string, SearchFieldDefinition>(StringComparer.OrdinalIgnoreCase);
        _byCanonical = new Dictionary<string, SearchFieldDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Fields)
        {
            _byCanonical[f.Canonical] = f;
            // Later fields win on alias collision, so game-specific fields (appended after the
            // shared defaults by WithGameFields) override a core alias when they claim it.
            foreach (var a in f.Aliases.Append(f.Canonical))
                _byAlias[a] = f;
        }
    }

    /// <summary>Resolve a raw field token to its canonical name. Falls back to the shared default
    /// normalization (so unknown-but-core tokens still map correctly), then identity.</summary>
    public string ResolveField(string rawField)
    {
        if (_byAlias.TryGetValue(rawField, out var d))
            return d.Canonical;
        return ScryfallQueryParser.NormalizeField(rawField.ToLowerInvariant());
    }

    /// <summary>The definition for a canonical name or alias, if this schema declares it.</summary>
    public SearchFieldDefinition? Find(string canonicalOrAlias)
    {
        if (_byCanonical.TryGetValue(canonicalOrAlias, out var byCanon))
            return byCanon;
        return _byAlias.TryGetValue(canonicalOrAlias, out var byAlias) ? byAlias : null;
    }

    /// <summary>True when this schema declares <paramref name="canonicalField"/> as a per-game field
    /// (data lives only in the catalog, not on CollectionCard).</summary>
    public bool IsGameSpecific(string canonicalField) =>
        _byCanonical.TryGetValue(canonicalField, out var d) && d.Kind == SearchFieldKind.GameSpecific;

    /// <summary>Apply a field's value-alias table (e.g. "f"→"Fire"). Returns the input unchanged when
    /// the field is unknown or has no matching alias.</summary>
    public string ResolveValue(string canonicalField, string value)
    {
        if (_byCanonical.TryGetValue(canonicalField, out var d) &&
            d.ValueAliases.TryGetValue(value, out var mapped))
            return mapped;
        return value;
    }
}
