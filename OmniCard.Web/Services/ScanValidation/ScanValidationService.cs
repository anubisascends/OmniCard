using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.CardMatching.Search;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Games;
using OmniCard.Shared.Scanning;
using OmniCard.Web.Services.TagRules;

namespace OmniCard.Web.Services.ScanValidation;

/// <summary>A validation rule failed a basic check; maps to HTTP 400.</summary>
public sealed class ScanValidationRuleException(IReadOnlyList<string> errors) : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Scan validation: check the cards in a scan list (a manual session or a background batch, still unsaved)
/// against a search query and report the ones that <b>don't</b> match, so a reviewer can catch a card that
/// doesn't belong ("everything in this box should be <c>set:dom</c>"). Also keeps the saved, per-game
/// rules the Scan page loads queries from.
///
/// <para>Queries go through the strict <see cref="TagRuleQueryValidator"/> (a typo must be an error, not a
/// silent name search that fails every card), with <c>tag:</c> allowed — a scan's tags are settled by the
/// time it's validated. Cards are matched in memory by <see cref="CollectionCardMatcher"/>, like tag rules
/// on unsaved scans.</para>
/// </summary>
public sealed class ScanValidationService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IEnumerable<ICardGameService> gameServices,
    TimeProvider time)
{
    private readonly IReadOnlyDictionary<CardGame, ICardGameService> _games = gameServices.ToDictionary(s => s.Game);

    // ---- Saved rules ----

    public IReadOnlyList<ScanValidationRuleDto> List(CardGame game)
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.ScanValidationRules.AsNoTracking().Where(r => r.Game == game).ToList()
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto).ToList();
    }

    /// <summary>Saves a rule, replacing the game's rule of the same name (case-insensitive) if there is one.</summary>
    public ScanValidationRuleDto Save(ScanValidationRuleInput input)
    {
        var errors = new List<string>();
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0) errors.Add("Enter a name.");
        else if (name.Length > ScanValidationRule.MaxNameLength)
            errors.Add($"The name is too long (max {ScanValidationRule.MaxNameLength} characters).");

        if (!Enum.TryParse<CardGame>(input.Game, ignoreCase: true, out var game) || !_games.ContainsKey(game))
        {
            errors.Add($"Unknown game '{input.Game}'.");
            throw new ScanValidationRuleException(errors);
        }

        errors.AddRange(Validate(game, input.Query));
        if (errors.Count > 0) throw new ScanValidationRuleException(errors);

        using var ctx = dbFactory.CreateDbContext();
        var now = time.GetUtcNow().UtcDateTime;
        // Name match in memory: SQLite (tests) compares case-sensitively, SQL Server doesn't.
        var rule = ctx.ScanValidationRules.Where(r => r.Game == game).AsEnumerable()
            .FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
        {
            rule = new ScanValidationRule { Game = game, CreatedAt = now };
            ctx.ScanValidationRules.Add(rule);
        }
        rule.Name = name;
        rule.Query = input.Query.Trim();
        rule.UpdatedAt = now;
        ctx.SaveChanges();
        return ToDto(rule);
    }

    public bool Delete(int id)
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.ScanValidationRules.Where(r => r.Id == id).ExecuteDelete() > 0;
    }

    // ---- Checking scans ----

    /// <summary>Problems with a query for <paramref name="game"/> (empty = valid).</summary>
    public IReadOnlyList<string> Validate(CardGame game, string? query)
    {
        if (query is { Length: > ScanValidationRule.MaxQueryLength })
            return [$"The query is too long (max {ScanValidationRule.MaxQueryLength} characters)."];
        return TagRuleQueryValidator.Validate(game, query, SchemaFor(game), allowTags: true);
    }

    /// <summary>The keys of the <paramref name="items"/> that don't match <paramref name="query"/>; nothing
    /// is checked when the query is invalid (its errors are returned instead).</summary>
    public ScanValidationResultDto Check(CardGame game, string query, IReadOnlyList<ScanTagRuleItem> items)
    {
        var errors = Validate(game, query);
        if (errors.Count > 0) return new ScanValidationResultDto([], errors);

        var schema = SchemaFor(game);
        if (ScryfallQueryParser.ParseFilter(query, schema) is not { } node)
            return new ScanValidationResultDto([], ["Enter a search query."]);

        var cards = ScanRuleCards.Build(game, items, _games);
        var resolve = CollectionCardMatcher.CreateResolver(_games, cards);
        var failed = new List<string>();
        for (var i = 0; i < items.Count; i++)
            // A scan with no identity can't satisfy anything, so it fails rather than slipping through.
            if (items[i].GameCardId.Length == 0 || !CollectionCardMatcher.Matches(cards[i], node, resolve))
                failed.Add(items[i].Key);
        return new ScanValidationResultDto(failed, []);
    }

    private SearchSchema SchemaFor(CardGame game) =>
        _games.TryGetValue(game, out var svc) && svc is IGameFieldResolver r ? r.SearchSchema : SharedSearchSchema.Default;

    private static ScanValidationRuleDto ToDto(ScanValidationRule r) => new(r.Id, r.Game.ToString(), r.Name, r.Query, r.UpdatedAt);
}
