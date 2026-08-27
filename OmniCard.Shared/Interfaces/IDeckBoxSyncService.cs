using OmniCard.Models;

namespace OmniCard.Interfaces;

/// <summary>Reconciles a Deck Box location against a target decklist: computes which cards to cut and
/// which to add (with candidate source locations), then applies the user's decisions as physical moves.</summary>
public interface IDeckBoxSyncService
{
    /// <summary>Diffs the deck box's current contents against <paramref name="targetEntries"/> and returns
    /// the cut/add plan. Keep/cut is decided by card name; Add sources are ordered exact-printing first.</summary>
    DeckBoxSyncPlan BuildPlan(int deckBoxId, List<DecklistEntry> targetEntries, CardGame game);

    /// <summary>Executes the user's resolved plan: moves added copies into the deck box (splitting source
    /// lots as needed), moves cut cards to their chosen location, and tags sideboard cards without moving them.</summary>
    void ApplySync(DeckBoxSyncCommitRequest request);
}
