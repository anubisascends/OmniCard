using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Api;

/// <summary>Read-only trade history (cards traded away), mirroring the desktop Trades view.</summary>
public sealed class TradesController(ITradeService trades) : ApiControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<TradeSummaryDto>> Get() =>
        trades.GetTrades().Select(ToDto).ToList();

    [HttpGet("{id:int}")]
    public ActionResult<TradeSummaryDto> GetOne(int id)
    {
        var t = trades.GetTrade(id);
        return t is null ? NotFound() : ToDto(t);
    }

    private static TradeSummaryDto ToDto(TradeSummary t) => new(
        t.Id, t.Label, t.Note, t.CreatedAt.ToString("o"), t.OutgoingValue, t.ReceivedValue,
        t.ValueDelta, t.ReplacementCount, !string.IsNullOrEmpty(t.ThumbnailPath),
        t.OutgoingCards.Select(c => new TradeCardDto(
            c.Game.ToString(), c.CardName, c.SetCode, c.SetName, c.CollectorNumber,
            c.Foil, c.IsOffDatabase, c.EstimatedValue)).ToList());
}
