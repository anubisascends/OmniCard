using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Collection;

public class CustomerService(IDbContextFactory<OmniCardDbContext> dbContextFactory) : ICustomerService
{
    public List<Customer> GetAll()
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Customers.AsNoTracking().OrderBy(c => c.Name).ToList();
    }

    public Customer? Get(int id)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        return ctx.Customers.AsNoTracking().FirstOrDefault(c => c.Id == id);
    }

    public Customer Create(Customer customer)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Customers.Add(customer);
        ctx.SaveChanges();
        return customer;
    }

    public void Update(Customer customer)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        ctx.Customers.Update(customer);
        ctx.SaveChanges();
    }

    public void Delete(int id)
    {
        using var ctx = dbContextFactory.CreateDbContext();
        var existing = ctx.Customers.FirstOrDefault(c => c.Id == id);
        if (existing is null) return;
        ctx.Customers.Remove(existing);
        ctx.SaveChanges();
    }
}
