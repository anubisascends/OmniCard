using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.Data;
using OmniCard.Shared.Sales;
using OmniCard.Shared.Security;
using OmniCard.Web.Api.Infrastructure;
using OmniCard.Web.Api.Mapping;
using OmniCard.Web.Services;
using OmniCard.Web.Services.Intake;

namespace OmniCard.Web.Api.Controllers;

/// <summary>Customer directory + CRUD. Creates/deletes go through <see cref="ICustomerService"/>;
/// edits load-then-patch through the DB factory so the SQL Server <c>RowVersion</c> concurrency token
/// is honored (the service's detached <c>Update</c> would fail the check). Intake buyers may list and
/// create customers too — picking the seller is part of buying their cards.</summary>
public sealed class CustomersController(
    ICustomerService customers,
    IDbContextFactory<OmniCardDbContext> dbFactory,
    StoreCreditService storeCredit) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.SalesCustomersView, Permissions.IntakeBuy)]
    public ActionResult<IReadOnlyList<CustomerDto>> Get()
    {
        var balances = storeCredit.Balances();
        return customers.GetAll()
            .Select(c => DtoMapping.ToDto(c) with { StoreCredit = balances.GetValueOrDefault(c.Id) })
            .ToList();
    }

    [HttpGet("{id:int}")]
    [RequirePermission(Permissions.SalesCustomersView, Permissions.IntakeBuy)]
    public ActionResult<CustomerDto> GetOne(int id)
    {
        var c = customers.Get(id);
        return c is null ? NotFound() : DtoMapping.ToDto(c) with { StoreCredit = storeCredit.Balance(id) };
    }

    /// <summary>A customer's store-credit balance and ledger (newest first).</summary>
    [HttpGet("{id:int}/credit")]
    [RequirePermission(Permissions.SalesCustomersView)]
    public ActionResult<StoreCreditDto> Credit(int id)
    {
        if (customers.Get(id) is null)
            return NotFound();
        var entries = storeCredit.Entries(id);
        return new StoreCreditDto(entries.Sum(e => e.Amount), entries
            .Select(e => new StoreCreditEntryDto(e.Id, e.Amount,
                DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc), e.CreatedByName, e.Reason, e.IntakeId))
            .ToList());
    }

    /// <summary>Manually credit (positive) or debit (negative) store credit, with a reason.</summary>
    [HttpPost("{id:int}/credit")]
    [RequirePermission(Permissions.SalesCustomersCredit)]
    public ActionResult<StoreCreditDto> AdjustCredit(int id, [FromBody] StoreCreditAdjustRequest request)
    {
        try
        {
            storeCredit.Adjust(id, request.Amount, request.Reason,
                AppAuthGate.CurrentUserId(HttpContext), AppAuthGate.CurrentUsername(HttpContext) ?? "");
        }
        catch (StoreCreditException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
        return Credit(id);
    }

    [HttpPost]
    [RequirePermission(Permissions.SalesCustomersCreate, Permissions.IntakeBuy)]
    public ActionResult<CustomerDto> Create([FromBody] CustomerUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });

        var created = customers.Create(new Customer
        {
            Name = request.Name.Trim(),
            Email = request.Email,
            Phone = request.Phone,
            AddressLine1 = request.AddressLine1,
            AddressLine2 = request.AddressLine2,
            City = request.City,
            State = request.State,
            PostalCode = request.PostalCode,
            Country = request.Country,
        });
        return CreatedAtAction(nameof(GetOne), new { id = created.Id }, DtoMapping.ToDto(created));
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.SalesCustomersEdit)]
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
        existing.AddressLine1 = request.AddressLine1;
        existing.AddressLine2 = request.AddressLine2;
        existing.City = request.City;
        existing.State = request.State;
        existing.PostalCode = request.PostalCode;
        existing.Country = request.Country;
        ctx.SaveChanges();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(Permissions.SalesCustomersDelete)]
    public IActionResult Delete(int id)
    {
        // Deleting the customer would take their credit ledger with it.
        if (storeCredit.Balance(id) != 0)
            return Conflict(new { error = "This customer still has store credit. Use it up or adjust it to zero first." });
        customers.Delete(id);
        return NoContent();
    }
}
