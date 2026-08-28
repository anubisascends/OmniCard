Per-game card-back images for the web binder reverse-side indicator.

When you view one side of a binder sheet, an empty pocket whose mirrored pocket on the
reverse side holds a card shows that game's card back (flipped horizontally via CSS). Drop
one PNG per game here; the filename must match the pattern card-back-{slug}.png where {slug}
comes from OmniCard.Shared/Models/CardBackAssets.Slug. Missing files degrade gracefully to a
generic CSS card-back background (the <img> removes itself on error).

Expected files (served at /img/... by the app's static-file middleware):

  card-back-mtg.png        Magic: The Gathering
  card-back-optcg.png      One Piece TCG
  card-back-riftbound.png  Riftbound
  card-back-pokemon.png     Pokemon
  card-back-yugioh.png     Yu-Gi-Oh!
  card-back-fftcg.png      Final Fantasy TCG

Use a standard trading-card aspect ratio (roughly 63:88). Card-back art is the game
publisher's intellectual property — supply images you have the right to bundle.
