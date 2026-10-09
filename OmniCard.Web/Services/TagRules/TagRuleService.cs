using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmniCard.Api.Contracts;
using OmniCard.CardMatching;
using OmniCard.CardMatching.Search;
using OmniCard.Collection;
using OmniCard.Data;
using OmniCard.Shared.Cards;
using OmniCard.Shared.Collection;
using OmniCard.Shared.Games;
using OmniCard.Shared.Tags;

namespace OmniCard.Web.Services.TagRules;

/// <summary>A rule failed <see cref="TagRuleQueryValidator"/> or a basic field check; maps to HTTP 400.</summary>
public sealed class TagRuleValidationException(IReadOnlyList<string> errors) : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Admin-defined auto-tagging rules (<see cref="TagRule"/>): CRUD, a preview / "Run now" against the
/// existing collection, the scan-review check of unsaved cards, and applying rules to cards an import
/// just created. Every path is additive — rule tags are merged with a card's tags, never replace them.
///
/// <para>Existing cards are matched in SQL by <see cref="CollectionQueryBuilder"/> (the Collection
/// page's search); unsaved scans and just-imported cards are matched in memory by
/// <see cref="CollectionCardMatcher"/>, whose game-field lookups are restricted to those few cards.</para>
/// </summary>
public sealed class TagRuleService(
    IDbContextFactory<OmniCardDbContext> dbFactory,
    IEnumerable<ICardGameService> gameServices,
    ITagService tags,
    TimeProvider time,
    ILogger<TagRuleService> logger)
{
    private const int PreviewSampleSize = 25;

    private readonly IReadOnlyDictionary<CardGame, ICardGameService> _games = gameServices.ToDictionary(s => s.Game);

    // ---- CRUD ----

    public IReadOnlyList<TagRuleDto> List(CardGame? game)
    {
        using var ctx = dbFactory.CreateDbContext();
        var rules = ctx.TagRules.AsNoTracking();
        if (game is { } g) rules = rules.Where(r => r.Game == g);
        return rules.ToList().OrderBy(r => r.Game).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToDto).ToList();
    }

    public TagRuleDto Create(TagRuleInput input)
    {
        var (game, tagList) = CheckInput(input);
        using var ctx = dbFactory.CreateDbContext();
        var now = time.GetUtcNow().UtcDateTime;
        var rule = new TagRule
        {
            Name = input.Name.Trim(),
            Game = game,
            Query = input.Query.Trim(),
            TagsJson = JsonSerializer.Serialize(tagList),
            Enabled = input.Enabled,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.TagRules.Add(rule);
        ctx.SaveChanges();
        return ToDto(rule);
    }

    /// <summary>Replaces a rule; null when it doesn't exist.</summary>
    public TagRuleDto? Update(int id, TagRuleInput input)
    {
        var (game, tagList) = CheckInput(input);
        using var ctx = dbFactory.CreateDbContext();
        var rule = ctx.TagRules.FirstOrDefault(r => r.Id == id);
        if (rule is null) return null;
        rule.Name = input.Name.Trim();
        rule.Game = game;
        rule.Query = input.Query.Trim();
        rule.TagsJson = JsonSerializer.Serialize(tagList);
        rule.Enabled = input.Enabled;
        rule.UpdatedAt = time.GetUtcNow().UtcDateTime;
        ctx.SaveChanges();
        return ToDto(rule);
    }

    public bool Delete(int id)
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.TagRules.Where(r => r.Id == id).ExecuteDelete() > 0;
    }

    /// <summary>Problems with a draft query for <paramref name="game"/> (empty = valid).</summary>
    public IReadOnlyList<string> Validate(CardGame game, string? query) =>
        TagRuleQueryValidator.Validate(game, query, SchemaFor(game));

    // ---- Existing collection: preview + Run now ----

    /// <summary>What a (possibly unsaved) rule would do to the cards already owned.</summary>
    public TagRulePreviewDto Preview(CardGame game, string query, IReadOnlyList<string> tagNames)
    {
        var errors = Validate(game, query);
        if (errors.Count > 0) return new TagRulePreviewDto(0, 0, [], errors);

        var wanted = CleanTags(tagNames);
        using var ctx = dbFactory.CreateDbContext();
        var matchIds = MatchingLotIds(ctx, game, query);
        var missing = MissingTags(matchIds, wanted);

        var sampleIds = missing.Keys.Take(PreviewSampleSize).ToList();
        var sample = CollectionQueryBuilder.BuildFilteredQuery(ctx, "", game, null, null)
            .Where(c => sampleIds.Contains(c.Id)).ToList()
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.SetCode)
            .Select(c => new TagRulePreviewCardDto(c.Id, c.Name, c.SetCode, c.Number, c.Container?.Name,
                c.Condition, c.IsFoil, missing[c.Id]))
            .ToList();
        return new TagRulePreviewDto(matchIds.Count, missing.Count, sample, []);
    }

    /// <summary>Applies a saved rule (enabled or not) to every owned card it matches. Null when the rule
    /// doesn't exist.</summary>
    public TagRuleRunResultDto? Run(int id)
    {
        TagRule? rule;
        using (var ctx = dbFactory.CreateDbContext())
            rule = ctx.TagRules.AsNoTracking().FirstOrDefault(r => r.Id == id);
        if (rule is null) return null;

        var errors = Validate(rule.Game, rule.Query);
        if (errors.Count > 0) throw new TagRuleValidationException(errors);

        List<int> matchIds;
        using (var ctx = dbFactory.CreateDbContext())
            matchIds = MatchingLotIds(ctx, rule.Game, rule.Query);

        var missing = MissingTags(matchIds, TagsOf(rule));
        foreach (var tag in missing.Values.SelectMany(t => t).Distinct(StringComparer.OrdinalIgnoreCase))
            tags.AddTagToLots(missing.Where(kv => kv.Value.Contains(tag, StringComparer.OrdinalIgnoreCase)).Select(kv => kv.Key), tag);

        logger.LogInformation("Tag rule {RuleId} ({Name}) tagged {Count} card(s)", rule.Id, rule.Name, missing.Count);
        return new TagRuleRunResultDto(missing.Count);
    }

    // ---- New cards: scan review + imports ----

    /// <summary>The enabled rules' tags for each unsaved scanned card of <paramref name="game"/>.</summary>
    public IReadOnlyList<ScanTagRuleResultDto> EvaluateScanItems(CardGame game, IReadOnlyList<ScanTagRuleItem> items)
    {
        var rules = EnabledRules(game);
        if (rules.Count == 0 || items.Count == 0)
            return items.Select(i => new ScanTagRuleResultDto(i.Key, [])).ToList();

        var cards = items.Select(i => new CollectionCard
        {
            Game = game,
            GameCardId = i.GameCardId,
            Name = i.Name,
            SetCode = i.SetCode,
            Number = i.CollectorNumber,
            Rarity = i.Rarity,
            Condition = string.IsNullOrWhiteSpace(i.Condition) ? "NM" : i.Condition,
            Language = CardLanguages.Normalize(i.Language) ?? CardLanguages.English,
            IsFoil = i.IsFoil,
            FoilType = i.IsFoil ? i.FoilType : null,
        }).ToList();

        if (_games.TryGetValue(game, out var svc))
            foreach (var card in cards.Where(c => c.GameCardId.Length > 0).DistinctBy(c => c.GameCardId))
            {
                CardAttributeExtractor.FillFromCatalog(card, svc);
                foreach (var twin in cards.Where(c => c.GameCardId == card.GameCardId && !ReferenceEquals(c, card)))
                    (twin.Color, twin.CardType) = (card.Color, card.CardType);
            }

        var tagsByCard = Evaluate(game, cards, rules);
        return items.Select((item, i) => new ScanTagRuleResultDto(item.Key, tagsByCard[i])).ToList();
    }

    /// <summary>Applies the enabled rules to lots an import just created, merging with their existing
    /// tags. Returns how many of them gained a tag. Never throws — the lots are already saved, so a rule
    /// failure is logged rather than failing the import.</summary>
    public int ApplyToNewLots(IReadOnlyCollection<int> lotIds)
    {
        if (lotIds.Count == 0) return 0;
        try
        {
            List<CollectionCard> cards;
            using (var ctx = dbFactory.CreateDbContext())
            {
                var ids = lotIds.Distinct().ToList();
                cards = CollectionQueryBuilder.BuildFilteredQuery(ctx, "", null, null, null)
                    .Where(c => ids.Contains(c.Id)).ToList();
            }

            var additions = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var tagged = new HashSet<int>();
            foreach (var group in cards.GroupBy(c => c.Game))
            {
                var rules = EnabledRules(group.Key);
                if (rules.Count == 0) continue;
                var list = group.ToList();
                // Some import paths store no type/colour; look them up so t:/c: rules still apply.
                if (_games.TryGetValue(group.Key, out var svc))
                    foreach (var card in list.Where(c => c.CardType is null || c.Color is null))
                        CardAttributeExtractor.FillFromCatalog(card, svc);
                var tagsByCard = Evaluate(group.Key, list, rules);
                for (var i = 0; i < list.Count; i++)
                    foreach (var tag in tagsByCard[i])
                    {
                        (additions.TryGetValue(tag, out var lots) ? lots : additions[tag] = []).Add(list[i].Id);
                        tagged.Add(list[i].Id);
                    }
            }

            foreach (var (tag, lots) in additions)
                tags.AddTagToLots(lots, tag);
            return tagged.Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply tag rules to {Count} new card(s)", lotIds.Count);
            return 0;
        }
    }

    // ---- Helpers ----

    /// <summary>Per card (parallel to <paramref name="cards"/>), the distinct tags of every rule it matches.</summary>
    private List<IReadOnlyList<string>> Evaluate(CardGame game, IReadOnlyList<CollectionCard> cards, IReadOnlyList<TagRule> rules)
    {
        var schema = SchemaFor(game);
        var resolve = CollectionCardMatcher.CreateResolver(_games, cards);
        var result = cards.Select(_ => new List<string>()).ToList();

        foreach (var rule in rules)
        {
            // Rules are validated on save, but re-check so one that a schema change broke is skipped
            // rather than falling back to a name search.
            if (TagRuleQueryValidator.Validate(game, rule.Query, schema).Count > 0) continue;
            if (ScryfallQueryParser.ParseFilter(rule.Query, schema) is not { } node) continue;
            var ruleTags = TagsOf(rule);
            for (var i = 0; i < cards.Count; i++)
            {
                if (!CollectionCardMatcher.Matches(cards[i], node, resolve)) continue;
                foreach (var tag in ruleTags)
                    if (!result[i].Contains(tag, StringComparer.OrdinalIgnoreCase)) result[i].Add(tag);
            }
        }
        return result.Select(r => (IReadOnlyList<string>)r).ToList();
    }

    private List<int> MatchingLotIds(OmniCardDbContext ctx, CardGame game, string query) =>
        CollectionQueryBuilder.BuildFilteredQuery(ctx, query, game, null, null, _games).Select(c => c.Id).ToList();

    /// <summary>For each lot lacking at least one of <paramref name="wanted"/>, the tags it lacks.</summary>
    private Dictionary<int, IReadOnlyList<string>> MissingTags(IReadOnlyList<int> lotIds, IReadOnlyList<string> wanted)
    {
        var existing = tags.GetTagsByLots(lotIds);
        var result = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var id in lotIds)
        {
            var have = existing.GetValueOrDefault(id) ?? [];
            var lacking = wanted.Where(t => !have.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
            if (lacking.Count > 0) result[id] = lacking;
        }
        return result;
    }

    private List<TagRule> EnabledRules(CardGame game)
    {
        using var ctx = dbFactory.CreateDbContext();
        return ctx.TagRules.AsNoTracking().Where(r => r.Game == game && r.Enabled).OrderBy(r => r.Id).ToList();
    }

    private SearchSchema SchemaFor(CardGame game) =>
        _games.TryGetValue(game, out var svc) && svc is IGameFieldResolver r ? r.SearchSchema : SharedSearchSchema.Default;

    private (CardGame Game, List<string> Tags) CheckInput(TagRuleInput input)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Name)) errors.Add("Enter a name.");
        else if (input.Name.Trim().Length > TagRule.MaxNameLength) errors.Add($"The name is too long (max {TagRule.MaxNameLength} characters).");

        var tagList = CleanTags(input.Tags);
        if (tagList.Count == 0) errors.Add("Add at least one tag.");

        if (!Enum.TryParse<CardGame>(input.Game, ignoreCase: true, out var game) || !_games.ContainsKey(game))
        {
            errors.Add($"Unknown game '{input.Game}'.");
            throw new TagRuleValidationException(errors);
        }

        errors.AddRange(Validate(game, input.Query));
        if (errors.Count > 0) throw new TagRuleValidationException(errors);
        return (game, tagList);
    }

    private static List<string> CleanTags(IEnumerable<string>? names) => (names ?? [])
        .Select(t => t.Trim()).Where(t => t.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> TagsOf(TagRule rule)
    {
        try { return CleanTags(JsonSerializer.Deserialize<List<string>>(rule.TagsJson)); }
        catch (JsonException) { return []; }
    }

    private static TagRuleDto ToDto(TagRule r) =>
        new(r.Id, r.Name, r.Game.ToString(), r.Query, TagsOf(r), r.Enabled, r.UpdatedAt);
}
