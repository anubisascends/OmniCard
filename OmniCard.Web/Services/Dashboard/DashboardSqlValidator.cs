using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace OmniCard.Web.Services.Dashboard;

/// <summary>
/// Checks that a SQL widget's text is a single read-only query before it ever reaches the database.
/// The T-SQL is parsed (Microsoft's ScriptDom, the same parser SQL Server tooling uses) rather than
/// pattern-matched, so comments, brackets and string literals can't smuggle anything past it.
///
/// <para><b>Allowed:</b> exactly one <c>SELECT</c> (with CTEs, joins, subqueries, <c>UNION</c>,
/// <c>GROUP BY</c>, window functions, <c>FOR JSON</c>…) over tables in the target database's
/// <c>dbo</c> schema. <b>Rejected:</b> anything else — DML/DDL, <c>EXEC</c>, several statements,
/// <c>SELECT … INTO</c>, cross-database / linked-server names, system schemas (<c>sys</c>,
/// <c>INFORMATION_SCHEMA</c>), <c>OPENROWSET</c>/<c>OPENQUERY</c>/<c>OPENDATASOURCE</c>, locking table
/// hints, and the <see cref="DeniedTables"/> holding sign-in secrets.</para>
///
/// <para>This is the first of three layers: <see cref="DashboardSqlRunner"/> also runs every query in
/// a transaction it always rolls back, and an administrator can point it at a read-only login
/// (<c>ConnectionStrings:OmniCardDashboard</c>).</para>
/// </summary>
public static class DashboardSqlValidator
{
    public const int MaxLength = 20_000;

    /// <summary>Tables no SQL widget may read (password hashes, setup keys).</summary>
    public static readonly IReadOnlySet<string> DeniedTables =
        new HashSet<string>(["Users"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<TableHintKind> AllowedHints =
    [
        TableHintKind.NoLock, TableHintKind.ReadUncommitted, TableHintKind.NoWait,
        TableHintKind.Index, TableHintKind.ForceSeek, TableHintKind.ForceScan,
    ];

    /// <summary>Null when <paramref name="sql"/> is an allowed query; otherwise why it isn't.</summary>
    public static string? Validate(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "Enter a SELECT query.";
        if (sql.Length > MaxLength)
            return $"Queries can be at most {MaxLength:N0} characters.";

        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        TSqlFragment fragment;
        IList<ParseError> errors;
        using (var reader = new StringReader(sql))
            fragment = parser.Parse(reader, out errors);
        if (errors.Count > 0)
            return $"Line {errors[0].Line}, column {errors[0].Column}: {errors[0].Message}";

        if (fragment is not TSqlScript script
            || script.Batches.Count != 1
            || script.Batches[0].Statements.Count != 1)
            return "Enter exactly one SELECT query.";
        if (script.Batches[0].Statements[0] is not SelectStatement select)
            return "Only SELECT queries are allowed.";
        if (select.Into is not null)
            return "SELECT … INTO isn't allowed: dashboard queries can't create tables.";

        var visitor = new Visitor();
        select.Accept(visitor);
        return visitor.Problem;
    }

    private sealed class Visitor : TSqlFragmentVisitor
    {
        public string? Problem { get; private set; }

        private void Reject(string problem) => Problem ??= problem;

        public override void Visit(NamedTableReference node)
        {
            CheckObject(node.SchemaObject, "table");
            base.Visit(node);
        }

        public override void Visit(SchemaObjectFunctionTableReference node)
        {
            CheckObject(node.SchemaObject, "function");
            base.Visit(node);
        }

        public override void Visit(FunctionCall node)
        {
            // A plain built-in (COUNT, SUM, FORMAT…) has no call target; dbo.MyFunction() is fine,
            // master.sys.fn_… or a column's method (x.value()) through another schema is not.
            if (node.CallTarget is MultiPartIdentifierCallTarget target)
            {
                var parts = target.MultiPartIdentifier.Identifiers;
                if (parts.Count > 1 || !IsDbo(parts[0].Value))
                    Reject($"Function \"{string.Join('.', parts.Select(p => p.Value))}.{node.FunctionName.Value}\" isn't allowed.");
            }
            base.Visit(node);
        }

        public override void Visit(TableHint node)
        {
            if (!AllowedHints.Contains(node.HintKind))
                Reject($"The table hint {node.HintKind.ToString().ToUpperInvariant()} isn't allowed.");
            base.Visit(node);
        }

        public override void Visit(OpenRowsetTableReference node) { Reject("OPENROWSET isn't allowed."); }
        public override void Visit(BulkOpenRowset node) { Reject("OPENROWSET isn't allowed."); }
        public override void Visit(InternalOpenRowset node) { Reject("OPENROWSET isn't allowed."); }
        public override void Visit(OpenRowsetCosmos node) { Reject("OPENROWSET isn't allowed."); }
        public override void Visit(OpenQueryTableReference node) { Reject("OPENQUERY isn't allowed."); }
        public override void Visit(AdHocTableReference node) { Reject("OPENDATASOURCE isn't allowed."); }
        public override void Visit(OpenXmlTableReference node) { Reject("OPENXML isn't allowed."); }

        private void CheckObject(SchemaObjectName name, string what)
        {
            var display = string.Join('.', name.Identifiers.Select(i => i.Value));
            if (name.ServerIdentifier is not null || name.DatabaseIdentifier is not null)
                Reject($"\"{display}\": other databases and servers can't be queried. Pick the database above the query instead.");
            else if (name.SchemaIdentifier is { } schema && !IsDbo(schema.Value))
                Reject($"\"{display}\": only the dbo schema can be queried.");
            else if (what == "table" && DeniedTables.Contains(name.BaseIdentifier.Value))
                Reject($"The {name.BaseIdentifier.Value} table can't be queried: it holds sign-in secrets.");
            // Unqualified compatibility views (sysobjects, syslogins…) resolve to the sys schema.
            else if (name.BaseIdentifier.Value.StartsWith("sys", StringComparison.OrdinalIgnoreCase))
                Reject($"\"{display}\": system views can't be queried.");
        }

        private static bool IsDbo(string schema) => string.Equals(schema, "dbo", StringComparison.OrdinalIgnoreCase);
    }
}
