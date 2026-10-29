using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Shared.Sales;

namespace OmniCard.Web.Services.Intake;

/// <summary>A store-credit change that can't be made (unknown customer, no reason, or a debit beyond the
/// balance). <see cref="StatusCode"/> is the HTTP status the API answers with.</summary>
public sealed class StoreCreditException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Customers' store-credit ledgers (<see cref="StoreCreditEntry"/>): the balance is the sum of a customer's
/// entries, and entries are only ever added. Writes run under one lock so two debits can't both pass the
/// balance check.
/// </summary>
public sealed class StoreCreditService(IDbContextFactory<OmniCardDbContext> dbFactory)
{
    private readonly object _writeLock = new();

    /// <summary>Every customer's non-zero balance.</summary>
    public Dictionary<int, decimal> Balances()
    {
        using var db = dbFactory.CreateDbContext();
        // Summed in memory: SQLite (tests) can't aggregate decimals, and ledgers are small.
        return db.StoreCreditEntries.AsNoTracking()
            .Select(e => new { e.CustomerId, e.Amount })
            .AsEnumerable()
            .GroupBy(e => e.CustomerId)
            .Select(g => (CustomerId: g.Key, Balance: g.Sum(e => e.Amount)))
            .Where(b => b.Balance != 0)
            .ToDictionary(b => b.CustomerId, b => b.Balance);
    }

    public decimal Balance(int customerId)
    {
        using var db = dbFactory.CreateDbContext();
        return Balance(db, customerId);
    }

    /// <summary>A customer's ledger, newest first.</summary>
    public IReadOnlyList<StoreCreditEntry> Entries(int customerId)
    {
        using var db = dbFactory.CreateDbContext();
        return db.StoreCreditEntries.AsNoTracking()
            .Where(e => e.CustomerId == customerId)
            .OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
            .ToList();
    }

    /// <summary>A manual credit (positive) or debit (negative) with a reason; returns the new balance.</summary>
    /// <exception cref="StoreCreditException">Unknown customer (404), zero amount or no reason (400), or a
    /// debit larger than the balance (409).</exception>
    public decimal Adjust(int customerId, decimal amount, string? reason, int? userId, string userName)
    {
        amount = Math.Round(amount, 2);
        if (amount == 0)
            throw new StoreCreditException("Enter an amount other than zero.");
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw new StoreCreditException("A reason is required.");
        if (trimmed.Length > StoreCreditEntry.MaxReasonLength)
            trimmed = trimmed[..StoreCreditEntry.MaxReasonLength];

        lock (_writeLock)
        {
            using var db = dbFactory.CreateDbContext();
            if (!db.Customers.Any(c => c.Id == customerId))
                throw new StoreCreditException("Customer not found.", 404);
            var balance = Balance(db, customerId);
            if (balance + amount < 0)
                throw new StoreCreditException($"The customer only has {balance:0.00} in store credit.", 409);

            db.StoreCreditEntries.Add(NewEntry(customerId, amount, trimmed, userId, userName, intakeId: null));
            db.SaveChanges();
            return balance + amount;
        }
    }

    /// <summary>Runs <paramref name="write"/> under the ledger's write lock (for callers that add entries in
    /// their own unit of work, like an intake).</summary>
    public T Locked<T>(Func<T> write)
    {
        lock (_writeLock)
            return write();
    }

    public static StoreCreditEntry NewEntry(int customerId, decimal amount, string reason, int? userId, string userName, int? intakeId) => new()
    {
        CustomerId = customerId,
        Amount = amount,
        Reason = reason,
        CreatedAt = DateTime.UtcNow,
        CreatedByUserId = userId,
        CreatedByName = userName,
        IntakeId = intakeId,
    };

    internal static decimal Balance(OmniCardDbContext db, int customerId) =>
        db.StoreCreditEntries.Where(e => e.CustomerId == customerId).Select(e => e.Amount).AsEnumerable().Sum();
}
