Per-game card-back images for the binder reverse-side indicator.

When you view one side of a binder sheet, an empty pocket whose mirrored pocket on the
reverse side holds a card shows that game's card back (flipped horizontally, as if you were
seeing the back of the card behind it). Drop one PNG per game here; the file stem must match
the slug below (see OmniCard.Shared/Models/CardBackAssets.Slug). Any that are missing fall
back to a generic vector card-back, so the app works fine before these are added.

Expected files (Build Action is already wired via a *.png wildcard in OmniCard.csproj):

  mtg.png        Magic: The Gathering
  optcg.png      One Piece TCG
  riftbound.png  Riftbound
  pokemon.png    Pokemon
  yugioh.png     Yu-Gi-Oh!
  fftcg.png      Final Fantasy TCG

Use a standard trading-card aspect ratio (roughly 63:88). These are shown at ~172x240 px.
Card-back art is the game publisher's intellectual property — supply images you have the
right to bundle.
