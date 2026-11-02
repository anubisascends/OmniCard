using System.Data;
using System.Data.Common;
using OmniCard.Api.Contracts;

namespace OmniCard.Web.Services.Dashboard;

public enum DashboardErrorKind { NotFound, Forbidden, BadRequest }

/// <summary>A dashboard rule was broken; the controller maps <see cref="Kind"/> to an HTTP status.</summary>
public sealed class DashboardException(DashboardErrorKind kind, string message) : Exception(message)
{
    public DashboardErrorKind Kind { get; } = kind;
}

/// <summary>A database SQL widgets can query: <see cref="Key"/> <c>main</c> (the unified store) or a
/// game id (that game's catalog). <see cref="Connect"/> returns a connection — opened or not; one that
/// comes back closed is opened and disposed by the runner, one already open is left as it is.</summary>
public sealed record DashboardSqlTarget(string Key, string Label, Func<DbConnection> Connect);

/// <summary>
/// Runs SQL widgets' queries. Each query is first checked by <see cref="DashboardSqlValidator"/>, then
/// runs inside a READ UNCOMMITTED transaction that is always rolled back (so it neither changes nor
/// blocks anything), with a <see cref="TimeoutSeconds"/> timeout and at most <see cref="MaxRows"/> rows.
/// </summary>
public sealed class DashboardSqlRunner(IReadOnlyList<DashboardSqlTarget> targets)
{
    public const string MainDatabase = "main";
    public const int MaxRows = 1000;
    public const int TimeoutSeconds = 30;
    private const int MaxBinaryBytes = 32;

    public IReadOnlyList<DashboardSqlTarget> Targets => targets;

    public DashboardSqlTarget Target(string? key) =>
        targets.FirstOrDefault(t => string.Equals(t.Key, string.IsNullOrWhiteSpace(key) ? MainDatabase : key, StringComparison.OrdinalIgnoreCase))
        ?? throw new DashboardException(DashboardErrorKind.BadRequest, $"Unknown database '{key}'.");

    public async Task<DashboardDataDto> RunAsync(string? database, string? sql, CancellationToken ct)
    {
        if (DashboardSqlValidator.Validate(sql) is { } problem)
            throw new DashboardException(DashboardErrorKind.BadRequest, problem);

        return await WithConnectionAsync(Target(database), async conn =>
        {
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadUncommitted, ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql!;
                cmd.CommandTimeout = TimeoutSeconds;
                await using var reader = await cmd.ExecuteReaderAsync(ct);

                var columns = Columns(reader);
                var rows = new List<List<object?>>();
                var truncated = false;
                while (await reader.ReadAsync(ct))
                {
                    if (rows.Count == MaxRows)
                    {
                        truncated = true;
                        break;
                    }
                    var row = new List<object?>(reader.FieldCount);
                    for (var i = 0; i < reader.FieldCount; i++)
                        row.Add(reader.IsDBNull(i) ? null : ToJsonValue(reader.GetValue(i)));
                    rows.Add(row);
                }
                return new DashboardDataDto { Columns = columns, Rows = rows, Truncated = truncated };
            }
            finally
            {
                await tx.RollbackAsync(CancellationToken.None);
            }
        }, ct);
    }

    /// <summary>The tables a query may read in <paramref name="database"/> (dbo only, minus the
    /// <see cref="DashboardSqlValidator.DeniedTables"/>), with their columns, A→Z.</summary>
    public async Task<DashboardSqlSchemaDto> SchemaAsync(string? database, CancellationToken ct) =>
        await WithConnectionAsync(Target(database), async conn =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = TimeoutSeconds;
            cmd.CommandText = conn.GetType().Name.Contains("Sqlite", StringComparison.Ordinal)
                ? "SELECT m.name, p.name, p.type FROM sqlite_master m JOIN pragma_table_info(m.name) p " +
                  "WHERE m.type = 'table' AND m.name NOT LIKE 'sqlite_%' ORDER BY m.name, p.cid"
                : "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS " +
                  "WHERE TABLE_SCHEMA = 'dbo' ORDER BY TABLE_NAME, ORDINAL_POSITION";
            var tables = new List<DashboardSqlTableDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var table = reader.GetString(0);
                if (DashboardSqlValidator.DeniedTables.Contains(table) || table.StartsWith("__", StringComparison.Ordinal))
                    continue;
                if (tables.Count == 0 || tables[^1].Name != table)
                    tables.Add(new DashboardSqlTableDto(table, []));
                tables[^1].Columns.Add(new DashboardSqlColumnDto(reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2)));
            }
            return new DashboardSqlSchemaDto(tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList());
        }, ct);

    private static async Task<T> WithConnectionAsync<T>(DashboardSqlTarget target, Func<DbConnection, Task<T>> work, CancellationToken ct)
    {
        var conn = target.Connect();
        var owned = conn.State != ConnectionState.Open;
        try
        {
            if (owned)
                await conn.OpenAsync(ct);
            return await work(conn);
        }
        catch (DbException ex)
        {
            throw new DashboardException(DashboardErrorKind.BadRequest, ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.InnerException is DbException inner)
        {
            throw new DashboardException(DashboardErrorKind.BadRequest, inner.Message);
        }
        finally
        {
            if (owned)
                await conn.DisposeAsync();
        }
    }

    /// <summary>The result's columns: unnamed ones become Column1…, repeated names get a _2, _3 suffix.</summary>
    private static List<DashboardColumnDto> Columns(DbDataReader reader)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var columns = new List<DashboardColumnDto>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (string.IsNullOrWhiteSpace(name))
                name = $"Column{i + 1}";
            var key = name;
            for (var n = 2; !used.Add(key); n++)
                key = $"{name}_{n}";
            columns.Add(new DashboardColumnDto(key, KindOf(reader, i)));
        }
        return columns;
    }

    private static string KindOf(DbDataReader reader, int i)
    {
        var dataType = SafeDataTypeName(reader, i);
        if (dataType is "money" or "smallmoney")
            return "money";
        var type = Nullable.GetUnderlyingType(reader.GetFieldType(i)) ?? reader.GetFieldType(i);
        if (type == typeof(bool))
            return "bool";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly))
            return "date";
        if (type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long)
            || type == typeof(decimal) || type == typeof(double) || type == typeof(float))
            return "number";
        return "text";
    }

    private static string SafeDataTypeName(DbDataReader reader, int i)
    {
        try { return reader.GetDataTypeName(i).ToLowerInvariant(); }
        catch (Exception) { return ""; }
    }

    /// <summary>A value as something the JSON writer can send: binary as short hex, non-finite floats as
    /// null, and anything unusual (Guid, TimeSpan, spatial…) as its text.</summary>
    internal static object? ToJsonValue(object value) => value switch
    {
        string or bool or int or long or short or byte or decimal => value,
        double d => double.IsFinite(d) ? d : null,
        float f => float.IsFinite(f) ? f : null,
        DateTime dt => dt,
        DateTimeOffset dto => dto,
        DateOnly date => date.ToString("yyyy-MM-dd"),
        byte[] bytes => "0x" + Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, MaxBinaryBytes))
                        + (bytes.Length > MaxBinaryBytes ? "…" : ""),
        _ => value.ToString(),
    };
}
