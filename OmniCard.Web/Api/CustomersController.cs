using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Web.Api;

/// <summary>Customer directory + CRUD. Creates/deletes go through <see cref="ICustomerService"/>;
/// edits load-then-patch through the DB factory so the SQL Server <c>RowVersion</c> concurrency token
/// is honored (the service's detached <c>Update</c> would fail the check).</summary>
public sealed class CustomersController(
    ICustomerService customers,
    IDbContextFactory<OmniCardDbContext> dbFactory) : ApiControllerBase
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

    [HttpPost]
    public ActionResult<CustomerDto> Create([FromBody] CustomerUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });

        var created = customers.Create(new Customer
        {
            Name = request.Name.Trim(),
            Email = request.Email,
            Phone = request.Phone,
            City = request.City,
            State = request.State,
        });
        return CreatedAtAction(nameof(GetOne), new { id = created.Id }, DtoMapping.ToDto(created));
    }

    [HttpPut("{id:int}")]
    public IActionResult Update(int id, [FromBody] CustomerUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });

        using var ctx = dbFactory.CreateDbContext();
        var existing = ctx.Customers.FirstOrDefault(c => c.Id == id);
        if (existing is null)
            return NotFound();

        existing.Name = request.Name.Trim();
        existing.Email = request.Email;
        existing.Phone = request.Phone;
        existing.City = request.City;
        existing.State = request.State;
        ctx.SaveChanges();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        customers.Delete(id);
        return NoContent();
    }
}
