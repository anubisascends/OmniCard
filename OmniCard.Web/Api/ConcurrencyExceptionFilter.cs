using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace OmniCard.Web.Api;

/// <summary>
/// Turns an EF Core optimistic-concurrency failure into an HTTP <c>409 Conflict</c>. On SQL Server
/// the unified store's mutable entities carry a <c>rowversion</c> token (see
/// <c>OmniCardDbContext.OnModelCreating</c>); when two clients edit the same row, the second
/// <c>SaveChanges</c> throws <see cref="DbUpdateConcurrencyException"/>. The SPA catches the 409 and
/// tells the user to reload. Registered globally for all controllers in <c>Program.cs</c>.
/// </summary>
public sealed class ConcurrencyExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is DbUpdateConcurrencyException)
        {
            context.Result = new ConflictObjectResult(new
            {
                error = "This item was changed by someone else. Reload and try again.",
            });
            context.ExceptionHandled = true;
        }
    }
}
