namespace OmniCard.Shared.Cards;

/// <summary>Display names of the <see cref="CardGame"/> values, for messages and the API's game list.</summary>
public static class CardGameNames
{
    public static string DisplayName(this CardGame game) => game switch
    {
        CardGame.Mtg => "Magic: The Gathering",
        CardGame.OnePiece => "One Piece TCG",
        CardGame.Riftbound => "Riftbound",
        CardGame.Pokemon => "Pokémon",
        CardGame.YuGiOh => "Yu-Gi-Oh!",
        CardGame.FinalFantasy => "Final Fantasy TCG",
        _ => game.ToString(),
    };
}
