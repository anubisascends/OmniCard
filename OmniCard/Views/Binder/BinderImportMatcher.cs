using System;
using System.Collections.Generic;
using System.Linq;
using OmniCard.Models;

namespace OmniCard.Views.Binder;

/// <summary>Reconcile logic for the import-driven binder audit: given an imported card and the pool
/// of the binder's own unplaced owned copies, decide whether to place an existing lot or create a
/// new card from the file. Kept as a static helper so it's unit-testable without standing up the
/// whole <see cref="BinderViewModel"/> and its dependencies.</summary>
public static class BinderImportMatcher
{
    /// <summary>Finds the owned unplaced lot an imported card should reuse: by GameCardId when the
    /// import has one, else by set code + collector number — requiring the foil treatment to agree in
    /// both cases. Returns null when the binder owns no matching unplaced copy (caller creates one).</summary>
    public static CollectionCard? FindOwnedMatch(IReadOnlyList<CollectionCard> pool, CollectionCard import)
    {
        if (!string.IsNullOrWhiteSpace(import.GameCardId))
        {
            var byId = pool.FirstOrDefault(c =>
                c.IsFoil == import.IsFoil &&
                string.Equals(c.GameCardId, import.GameCardId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null) return byId;
        }

        if (!string.IsNullOrWhiteSpace(import.SetCode) && !string.IsNullOrWhiteSpace(import.Number))
        {
            return pool.FirstOrDefault(c =>
                c.IsFoil == import.IsFoil &&
                Norm(c.SetCode) == Norm(import.SetCode) &&
                Norm(c.Number) == Norm(import.Number));
        }

        return null;
    }

    /// <summary>Builds a <see cref="CardMatch"/> from an imported card, for creating a fresh lot when
    /// no owned copy matched.</summary>
    public static CardMatch ToCardMatch(CollectionCard c) => new()
    {
        Name = c.Name,
        SetCode = c.SetCode,
        SetName = c.SetName,
        CollectorNumber = c.Number,
        Rarity = c.Rarity,
        ImageUri = c.ImageUri,
        GameSpecificId = c.GameCardId,
        Source = new object(),
    };

    private static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();
}
