using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.CardMatching;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Imaging;
using OmniCard.Interfaces;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class TcgCsvScanRoutingTests
{
    [Fact]
    public void CardService_ResolvesPokemonService_AndRoutesMatch()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(conn).Options;
        var factory = new PkFactory(options);
        using (var ctx = factory.CreateDbContext())
        {
            ctx.Database.EnsureCreated(); ctx.MarkMigrationComplete();
            ctx.Cards.Add(new TcgCsvCard { ProductId = 5, Game = CardGame.Pokemon, Name = "Pikachu", SetCode = "BS", ImageHash = 0b1UL });
            ctx.SaveChanges();
        }
        var dp = new Moq.Mock<IDataPathService>();
        dp.Setup(d => d.DataDirectory).Returns(Path.Combine(Path.GetTempPath(), "route-" + Guid.NewGuid().ToString("N")));
        var pokemon = new PokemonService(new NoHttp(), factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance), dp.Object, NullLogger<PokemonService>.Instance);

        var svc = pokemon as ICardGameService;
        var match = svc.FindClosestMatch(0b1UL);
        Assert.Equal("5", match!.GameSpecificId);
        Assert.Equal(CardGame.Pokemon, svc.Game);
    }

    private class PkFactory(DbContextOptions<PokemonDbContext> o) : IDbContextFactory<PokemonDbContext>
    { public PokemonDbContext CreateDbContext() => new(o); }
    private class NoHttp : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }
}
