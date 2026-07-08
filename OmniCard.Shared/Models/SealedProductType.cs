namespace OmniCard.Models;

public enum SealedProductType
{
    // Cases
    Case,

    // Boxes
    PlayBoosterBox,
    DraftBoosterBox,
    SetBoosterBox,
    CollectorBoosterBox,
    ThemeBoosterBox,
    BoosterBox,

    // Packs
    PlayBoosterPack,
    DraftBoosterPack,
    SetBoosterPack,
    CollectorBoosterPack,
    ThemeBoosterPack,
    BoosterPack,
    PromoPack,

    // Bundles & Kits
    Bundle,
    GiftBundle,
    FatPack,
    PrereleaseKit,
    StarterKit,

    // Decks & Fixed Products
    CommanderDeck,
    PlaneswalkerDeck,
    IntroPack,
    ThemeDeck,
    IntroDeck,
    WelcomeDeck,
    FixedPack,

    // Special Products
    SecretLair,
    FromTheVault,
    BlisterPack,

    // Terminal
    Card,
}
