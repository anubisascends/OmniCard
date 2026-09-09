using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Shared.Collection;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Api.Mapping;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Dashboard metrics: holdings valuation + realized P&amp;L.</summary>
public sealed class DashboardController(IAnalyticsService analytics) : ApiControllerBase
{
    [HttpGet]
    public ActionResult<DashboardDto> Get()
    {
        var holdings = analytics.GetHoldings();
        var realized = analytics.GetRealized();
        return DtoMapping.ToDto(holdings, realized);
    }
}
