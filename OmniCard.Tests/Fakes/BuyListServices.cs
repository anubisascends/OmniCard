using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Data;
using OmniCard.Shared.BuyList;
using OmniCard.Web.Services.BuyList;

namespace OmniCard.Tests.Fakes;

/// <summary>In-memory <see cref="IBuyListSettingsService"/>.</summary>
public sealed class FakeBuyListSettings : IBuyListSettingsService
{
    public BuyListSettings Value { get; set; } = new();
    public BuyListSettings Get() => Value;
    public void Save(BuyListSettings settings) => Value = settings;
}

/// <summary>A real <see cref="BuyListService"/> over the test database.</summary>
public static class BuyListServices
{
    public static BuyListService Create(IDbContextFactory<OmniCardDbContext> factory, IBuyListSettingsService? settings = null) =>
        new(factory, settings ?? new FakeBuyListSettings(), NullLogger<BuyListService>.Instance);
}
