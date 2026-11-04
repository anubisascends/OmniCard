using OmniCard.Shared.Cards;

namespace OmniCard.Shared.BuyList;

/// <summary>Admin settings for the Buy Lists (<c>buy-list-settings.json</c> in the data directory).</summary>
public sealed class BuyListSettings
{
    /// <summary>Per game, the location wanted copies go to when committed or bought, instead of the normal
    /// scan or intake location. A game with no entry keeps them with the rest.</summary>
    public Dictionary<CardGame, int> SetAsideLocationIds { get; set; } = [];
}

/// <summary>Reads and writes <see cref="BuyListSettings"/>.</summary>
public interface IBuyListSettingsService
{
    BuyListSettings Get();
    void Save(BuyListSettings settings);
}
