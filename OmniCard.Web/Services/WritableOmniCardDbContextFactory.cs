using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Web.Data;

namespace OmniCard.Web.Services;

/// <summary>
/// Hands out <see cref="OmniCardDbContext"/> instances against the SQL Server unified store. On SQL
/// Server there's no read-only/writable split (the server handles concurrent readers and writers),
/// so this simply builds contexts from the same connection string as the DI-registered factory. It
/// remains a distinct type only because the binder-editor write services take it by concrete type.
/// </summary>
public sealed class WritableOmniCardDbContextFactory : IDbContextFactory<OmniCardDbContext>
{
    private readonly DbContextOptions<OmniCardDbContext> _options;

    public WritableOmniCardDbContextFactory(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<OmniCardDbContext>();
        SqlServerDb.Configure(builder, connectionString);
        _options = builder.Options;
    }

    public OmniCardDbContext CreateDbContext() => new(_options);
}
