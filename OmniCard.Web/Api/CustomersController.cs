using Microsoft.AspNetCore.Mvc;
using OmniCard.Api.Contracts;
using OmniCard.Interfaces;

namespace OmniCard.Web.Api;

/// <summary>Customer directory.</summary>
public sealed class CustomersController(ICustomerService customers) : ApiControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<CustomerDto>> Get() =>
        customers.GetAll().Select(DtoMapping.ToDto).ToList();

    [HttpGet("{id:int}")]
    public ActionResult<CustomerDto> GetOne(int id)
    {
        var c = customers.Get(id);
        return c is null ? NotFound() : DtoMapping.ToDto(c);
    }
}
