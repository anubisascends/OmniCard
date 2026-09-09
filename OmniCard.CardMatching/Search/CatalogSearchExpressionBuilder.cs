using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace OmniCard.CardMatching.Search;

/// <summary>
/// Per-game map from canonical field name to an EF-translatable predicate builder over the game's
/// catalog entity. Games register their columns once (via the <see cref="Str"/>/<see cref="Num"/>
/// helpers or a custom lambda); <see cref="CatalogSearchExpressionBuilder"/> walks a parsed
/// <see cref="FilterNode"/> tree and composes them, applying the schema's value aliases first.
/// </summary>
public sealed class CatalogFieldMap<TEntity>
{
    private readonly Dictionary<string, Func<ParameterExpression, ComparisonOp, string, Expression>> _fields =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fallback used for bare words and any field the map doesn't declare (typically name).</summary>
    public Func<ParameterExpression, ComparisonOp, string, Expression> NamePredicate { get; init; } = null!;

    public CatalogFieldMap<TEntity> Field(string canonical, Func<ParameterExpression, ComparisonOp, string, Expression> build)
    {
        _fields[canonical] = build;
        return this;
    }

    /// <summary>Register a string column: <c>:</c> = contains, <c>=</c> = exact, <c>!=</c> = not-equal
    /// (all case-insensitive via LIKE). Ordering operators degrade to contains.</summary>
    public CatalogFieldMap<TEntity> Str(string canonical, string propertyName) =>
        Field(canonical, (p, op, v) => CatalogSearchExpressionBuilder.StringPredicate(p, propertyName, op, v));

    /// <summary>Register a nullable numeric column (int?/decimal?): supports :,=,!=,&lt;,&gt;,&lt;=,&gt;=.
    /// Non-numeric values never match.</summary>
    public CatalogFieldMap<TEntity> Num(string canonical, string propertyName) =>
        Field(canonical, (p, op, v) => CatalogSearchExpressionBuilder.NumericPredicate(p, propertyName, op, v));

    internal Expression Build(ParameterExpression p, string canonicalField, ComparisonOp op, string value)
    {
        if (_fields.TryGetValue(canonicalField, out var f))
            return f(p, op, value);
        return NamePredicate(p, op, value);
    }
}

/// <summary>
/// Compiles a parsed <see cref="FilterNode"/> tree into an <c>Expression&lt;Func&lt;TEntity,bool&gt;&gt;</c>
/// for a game's catalog, using its <see cref="CatalogFieldMap{TEntity}"/>. Gives every column-backed
/// game (MTG/OPTCG/Riftbound) uniform operator + AND/OR/NOT support, replacing bespoke
/// <c>string.StartsWith</c> loops. Value aliases (e.g. <c>f</c>→<c>Fire</c>) are applied here from the
/// game's <see cref="SearchSchema"/> before the predicate is built.
/// </summary>
public static class CatalogSearchExpressionBuilder
{
    public static Expression<Func<TEntity, bool>>? Build<TEntity>(FilterNode? node, SearchSchema schema, CatalogFieldMap<TEntity> map)
    {
        if (node is null) return null;
        var p = Expression.Parameter(typeof(TEntity), "c");
        var body = BuildNode(p, node, schema, map);
        return Expression.Lambda<Func<TEntity, bool>>(body, p);
    }

    private static Expression BuildNode<TEntity>(ParameterExpression p, FilterNode node, SearchSchema schema, CatalogFieldMap<TEntity> map) => node switch
    {
        FieldFilter f => BuildField(p, f, schema, map),
        AndFilter and => and.Children.Select(c => BuildNode(p, c, schema, map)).Aggregate(Expression.AndAlso),
        OrFilter or => or.Children.Select(c => BuildNode(p, c, schema, map)).Aggregate(Expression.OrElse),
        NotFilter not => Expression.Not(BuildNode(p, not.Inner, schema, map)),
        _ => Expression.Constant(true),
    };

    private static Expression BuildField<TEntity>(ParameterExpression p, FieldFilter f, SearchSchema schema, CatalogFieldMap<TEntity> map)
    {
        var value = schema.ResolveValue(f.Field, f.Value);
        var expr = map.Build(p, f.Field, f.Op, value);
        return f.Negated ? Expression.Not(expr) : expr;
    }

    /// <summary>A lambda for a single canonical field predicate (value already alias-resolved). Used by
    /// <see cref="IGameFieldResolver.ResolveFieldCardIds"/> on column-backed games.</summary>
    public static Expression<Func<TEntity, bool>> SingleField<TEntity>(string canonicalField, ComparisonOp op, string resolvedValue, CatalogFieldMap<TEntity> map)
    {
        var p = Expression.Parameter(typeof(TEntity), "c");
        return Expression.Lambda<Func<TEntity, bool>>(map.Build(p, canonicalField, op, resolvedValue), p);
    }

    // ---- Shared predicate helpers (EF-translatable) ----

    private static readonly System.Reflection.MethodInfo LikeMethod =
        typeof(DbFunctionsExtensions).GetMethod(nameof(DbFunctionsExtensions.Like),
            [typeof(DbFunctions), typeof(string), typeof(string)])!;

    public static Expression Like(Expression stringProp, string pattern) =>
        Expression.Call(LikeMethod, Expression.Property(null, typeof(EF), nameof(EF.Functions)), stringProp, Expression.Constant(pattern));

    /// <summary>String column predicate. Null-safe (a null column never matches a positive term).</summary>
    public static Expression StringPredicate(ParameterExpression p, string propertyName, ComparisonOp op, string value)
    {
        var prop = Expression.Property(p, propertyName);
        var notNull = prop.Type == typeof(string)
            ? (Expression)Expression.NotEqual(prop, Expression.Constant(null, typeof(string)))
            : Expression.Constant(true);

        return op switch
        {
            ComparisonOp.Exact => Expression.AndAlso(notNull, Like(prop, value)),
            ComparisonOp.NotEqual => Expression.OrElse(
                prop.Type == typeof(string) ? Expression.Equal(prop, Expression.Constant(null, typeof(string))) : Expression.Constant(false),
                Expression.Not(Like(prop, value))),
            _ => Expression.AndAlso(notNull, Like(prop, $"%{value}%")),
        };
    }

    /// <summary>Numeric column predicate for int?/decimal?/int/decimal columns. Supports ordering.</summary>
    public static Expression NumericPredicate(ParameterExpression p, string propertyName, ComparisonOp op, string value)
    {
        var prop = Expression.Property(p, propertyName);
        var underlying = Nullable.GetUnderlyingType(prop.Type) ?? prop.Type;

        if (!TryConvert(value, underlying, out var constVal))
            return Expression.Constant(false);

        Expression left = prop;
        Expression right = Expression.Constant(constVal, prop.Type);
        // For nullable columns, EF handles the comparison; but a null column should never match a
        // positive predicate, so guard NotEqual specially.
        var hasValue = Nullable.GetUnderlyingType(prop.Type) is not null
            ? (Expression)Expression.Property(prop, "HasValue")
            : Expression.Constant(true);

        return op switch
        {
            ComparisonOp.Exact or ComparisonOp.Contains => Expression.AndAlso(hasValue, Expression.Equal(left, right)),
            ComparisonOp.NotEqual => Expression.AndAlso(hasValue, Expression.NotEqual(left, right)),
            ComparisonOp.LessThan => Expression.AndAlso(hasValue, Expression.LessThan(left, right)),
            ComparisonOp.GreaterThan => Expression.AndAlso(hasValue, Expression.GreaterThan(left, right)),
            ComparisonOp.LessOrEqual => Expression.AndAlso(hasValue, Expression.LessThanOrEqual(left, right)),
            ComparisonOp.GreaterOrEqual => Expression.AndAlso(hasValue, Expression.GreaterThanOrEqual(left, right)),
            _ => Expression.AndAlso(hasValue, Expression.Equal(left, right)),
        };
    }

    private static bool TryConvert(string value, Type underlying, out object? result)
    {
        result = null;
        if (underlying == typeof(int))
        {
            if (int.TryParse(value, out var i)) { result = i; return true; }
            return false;
        }
        if (underlying == typeof(decimal))
        {
            if (decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d)) { result = d; return true; }
            return false;
        }
        if (underlying == typeof(double))
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var db)) { result = db; return true; }
            return false;
        }
        return false;
    }
}
