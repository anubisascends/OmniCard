using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OmniCard.Collection.Inventory;
using OmniCard.Data;
using OmniCard.Shared.Games;
using OmniCard.Web.Services.TagRules;

namespace OmniCard.Tests.Fakes;

/// <summary>Builds a real <see cref="TagRuleService"/> over a test store, for tests of the scan/import
/// paths that apply tag rules.</summary>
public static class TagRuleServices
{
    public static TagRuleService Create(IDbContextFactory<OmniCardDbContext> factory, params ICardGameService[] games) =>
        new(factory, games, new TagService(factory), TimeProvider.System, NullLogger<TagRuleService>.Instance);
}
