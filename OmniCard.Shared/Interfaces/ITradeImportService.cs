namespace OmniCard.Interfaces;

/// <summary>Applies pending trade records dropped by the web companion app (see
/// <see cref="OmniCard.Models.TradeRecord"/>) to the collection. Called once at desktop startup,
/// after the unified-store migration.</summary>
public interface ITradeImportService
{
    /// <summary>Applies every unprocessed trade record under
    /// <see cref="IDataPathService.TradesDirectory"/>. Returns the number applied. Safe to call
    /// repeatedly — already-processed records are skipped.</summary>
    int ImportPendingTrades();
}
