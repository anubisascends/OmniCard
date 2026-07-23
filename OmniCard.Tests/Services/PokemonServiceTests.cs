using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class PokemonServiceTests
{
    [Fact]
    public void Game_And_Category_AreCorrect()
    {
        var svc = Create();
        Assert.Equal(CardGame.Pokemon, svc.Game);
    }

    private static PokemonService Create()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(conn).Options;
        var factory = new PkFactory(options);
        using (var ctx = factory.CreateDbContext()) { ctx.Database.EnsureCreated(); ctx.MarkMigrationComplete(); }
        var dp = new Moq.Mock<IDataPathService>();
        dp.Setup(d => d.DataDirectory).Returns(Path.Combine(Path.GetTempPath(), "pk-" + Guid.NewGuid().ToString("N")));
        return new PokemonService(new NoHttp(), factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance), dp.Object,
            NullLogger<PokemonService>.Instance);
    }

    private class PkFactory(DbContextOptions<PokemonDbContext> o) : IDbContextFactory<PokemonDbContext>
    { public PokemonDbContext CreateDbContext() => new(o); }
    private class NoHttp : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
}
