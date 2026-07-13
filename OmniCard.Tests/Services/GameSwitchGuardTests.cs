using OmniCard.Models;
using System.Collections.ObjectModel;

namespace OmniCard.Tests.Services;

public class GameSwitchGuardTests
{
    [Fact]
    public void HasPendingScans_ReturnsTrue_WhenScannedCardsNotEmpty()
    {
        var scannedCards = new ObservableCollection<ScannedCard>
        {
            new ScannedCard { TempImagePath = "/tmp/test.png", Hash = 0x1234 }
        };

        Assert.True(scannedCards.Count > 0);
    }

    [Fact]
    public void HasPendingScans_ReturnsFalse_WhenScannedCardsEmpty()
    {
        var scannedCards = new ObservableCollection<ScannedCard>();

        Assert.False(scannedCards.Count > 0);
    }
}
