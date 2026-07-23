using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OmniCard.Data;
using OmniCard.Models;

namespace OmniCard.Tests.Services;

public class TcgCsvSchemaTests
{
    [Fact]
    public void EnsureCreated_ThenApplySchemaUpgrades_IsIdempotent()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(conn).Options;

        using var ctx = new PokemonDbContext(options);
        ctx.Database.EnsureCreated();
        ctx.ApplySchemaUpgrades();   // must not throw on already-present columns
        ctx.ApplySchemaUpgrades();   // second call also fine
        ctx.MarkMigrationComplete();

        Assert.Equal(TcgCsvDbContext.TcgCsvSchemaVersion, ctx.GetSchemaVersion());

        ctx.Cards.Add(new TcgCsvCard { ProductId = 1, Game = CardGame.Pokemon, Name = "Pikachu", ExtendedDataJson = "[]" });
        ctx.SaveChanges();
        Assert.Equal("Pikachu", ctx.Cards.Single(c => c.ProductId == 1).Name);
    }
}
