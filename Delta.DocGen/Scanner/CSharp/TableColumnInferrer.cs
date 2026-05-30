using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Delta.DocGen.Scanner.CSharp;

/// <summary>
/// Infers data table column names from common Reqnroll/SpecFlow Table access patterns
/// in a step definition method body, using Roslyn syntax analysis only (no semantic model).
/// </summary>
public static class TableColumnInferrer
{
    private static readonly HashSet<string> TableGetterNames = new(StringComparer.Ordinal)
    {
        "GetString", "GetInt", "GetInt32", "GetInt64", "GetDecimal",
        "GetDouble", "GetFloat", "GetBool", "GetBoolean", "GetDateTime", "GetLong"
    };

    /// <summary>
    /// Returns ordered, deduplicated column names inferred from the method body,
    /// or an empty list if none can be determined.
    /// </summary>
    /// <param name="method">The step definition method to analyse.</param>
    /// <param name="tableParamName">The name of the Table parameter in the method signature.</param>
    public static IReadOnlyList<string> Infer(
        MethodDeclarationSyntax method,
        string tableParamName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();

        // Identify loop variables that iterate over the table's rows.
        var rowVarNames = CollectRowIterationVars(method, tableParamName);

        foreach (var node in method.DescendantNodes())
        {
            switch (node)
            {
                // Pattern 1: table.GetString("col"), table.GetInt("col"), etc.
                case InvocationExpressionSyntax inv
                    when IsTableGetterCall(inv, tableParamName, out var colName):
                    AddIfNew(colName!, seen, ordered);
                    break;

                // Pattern 2: table["col"] or row["col"]
                case ElementAccessExpressionSyntax ea
                    when IsTableOrRowIndexer(ea, tableParamName, rowVarNames, out var colName):
                    AddIfNew(colName!, seen, ordered);
                    break;
            }
        }

        return ordered.AsReadOnly();
    }

    private static bool IsTableGetterCall(
        InvocationExpressionSyntax inv,
        string tableParamName,
        out string? columnName)
    {
        columnName = null;
        if (inv.Expression is not MemberAccessExpressionSyntax ma) return false;
        if (ma.Expression.ToString() != tableParamName) return false;
        if (!TableGetterNames.Contains(ma.Name.Identifier.Text)) return false;

        var args = inv.ArgumentList.Arguments;
        if (args.Count == 0) return false;

        // First argument must be a string literal
        if (args[0].Expression is not LiteralExpressionSyntax lit
            || !lit.IsKind(SyntaxKind.StringLiteralExpression))
            return false;

        columnName = lit.Token.ValueText;
        return !string.IsNullOrWhiteSpace(columnName);
    }

    private static bool IsTableOrRowIndexer(
        ElementAccessExpressionSyntax ea,
        string tableParamName,
        IReadOnlySet<string> rowVarNames,
        out string? columnName)
    {
        columnName = null;

        var target = ea.Expression.ToString();
        if (target != tableParamName && !rowVarNames.Contains(target)) return false;

        if (ea.ArgumentList.Arguments.Count != 1) return false;
        if (ea.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax lit
            || !lit.IsKind(SyntaxKind.StringLiteralExpression))
            return false;

        columnName = lit.Token.ValueText;
        return !string.IsNullOrWhiteSpace(columnName);
    }

    /// <summary>
    /// Finds variables declared as loop vars iterating over the table's Rows.
    /// Handles: foreach (var row in table.Rows) and foreach (var row in table.Rows.Skip(...)) etc.
    /// </summary>
    private static IReadOnlySet<string> CollectRowIterationVars(
        MethodDeclarationSyntax method, string tableParamName)
    {
        var vars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fe in method.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            var expr = fe.Expression.ToString();
            // Matches: table.Rows, table.Rows.Skip(...), table.Rows.Take(...) etc.
            if (expr == $"{tableParamName}.Rows"
                || expr.StartsWith($"{tableParamName}.Rows.", StringComparison.Ordinal)
                || expr.StartsWith($"{tableParamName}.Rows[", StringComparison.Ordinal))
                vars.Add(fe.Identifier.Text);
        }
        return vars;
    }

    private static void AddIfNew(string name, HashSet<string> seen, List<string> ordered)
    {
        if (seen.Add(name)) ordered.Add(name);
    }
}
