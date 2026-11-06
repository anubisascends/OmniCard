using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Imaging;
using OmniCard.Shared.Games;
using OmniCard.Shared.Settings;
using OmniCard.CardMatching.Games;
using OmniCard.Data.Catalogs;
using OmniCard.Tests.Fakes;
using SkiaSharp;

namespace OmniCard.Tests.Services.Games;

public class RiftboundHashingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<RiftboundDbContext> _factory;
    private readonly string _dataDir;

    public RiftboundHashingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<RiftboundDbContext>().UseSqlite(_connection).Options;
        _factory = new Factory(options);
        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();
        ctx.MarkMigrationComplete();
        ctx.Cards.Add(new RiftboundCard
        {
            Id = "c1",
            RiftboundId = "ogn-1-298",
            CollectorNumber = 1,
            Name = "Test",
            SetId = "OGN",
            SetName = "Origins",
            CardImageUri = "https://cdn/c1.png",
        });
        ctx.SaveChanges();

        _dataDir = Path.Combine(Path.GetTempPath(), "rift-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }

    private static byte[] PngBytes() =>
        TestImages.Png(64, 90, SKColors.White, g =>
        {
            using var brush = TestImages.Fill(SKColors.Black);
            g.DrawRect(SKRect.Create(10, 10, 30, 40), brush);
        });

    [Fact]
    public async Task ComputeImageHashes_PopulatesHashesAndPath()
    {
        var png = PngBytes();
        var handler = new StubHandler(png);
        var dataPath = new Moq.Mock<IDataPathService>();
        dataPath.Setup(d => d.DataDirectory).Returns(_dataDir);
        var svc = new RiftboundService(
            new FakeFactory(handler), _factory,
            new PerceptualHashService(NullLogger<PerceptualHashService>.Instance),
            dataPath.Object, NullLogger<RiftboundService>.Instance);

        await svc.ComputeImageHashesAsync(forceAll: true);

        using var ctx = _factory.CreateDbContext();
        var row = ctx.Cards.Single();
        Assert.NotNull(row.ImageHash);
        Assert.NotNull(row.EdgeHash);
        Assert.Equal("riftbound-art/c1.png", row.LocalImagePath);
        Assert.True(File.Exists(Path.Combine(_dataDir, "riftbound-art", "c1.png")));
    }

    private class StubHandler(byte[] png) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(png) });
    }
    private class FakeFactory(HttpMessageHandler h) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(h); }
    private class Factory(DbContextOptions<RiftboundDbContext> o) : IDbContextFactory<RiftboundDbContext>
    { public RiftboundDbContext CreateDbContext() => new(o); }
}
