using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OmniCard.Data;

namespace OmniCard.Web.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build an <see cref="OmniCardDbContext"/>
/// against SQL Server without spinning up the whole web host. Uses the default local connection
/// string (or the <c>OMNICARD_CONNECTION</c> env var if set).
/// </summary>
public sealed class DesignTimeOmniCardDbContextFactory : IDesignTimeDbContextFactory<OmniCardDbContext>
{
    public OmniCardDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("OMNICARD_CONNECTION")
                   ?? SqlServerDb.DefaultConnectionString;
        var options = new DbContextOptionsBuilder<OmniCardDbContext>();
        SqlServerDb.Configure(options, conn);
        return new OmniCardDbContext(options.Options);
    }
}
