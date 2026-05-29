using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Scanner.CSharp;

public static class StepDefinitionExtractor
{
    private static readonly HashSet<string> StepAttributeNames =
        new(StringComparer.Ordinal) { "Given", "When", "Then", "StepDefinition" };

    private static readonly Regex PlaceholderPattern =
        new(@"\{[^}]+\}", RegexOptions.Compiled);

    public static IReadOnlyList<RawStep> Extract(
        string relativePath, string root, IDocGenLogger logger)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var text = File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
        var compilationUnit = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();

        var steps = new List<RawStep>();

        foreach (var method in compilationUnit.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            // Track (Type, Pattern) tuples seen on the current method so identical
            // attributes stacked on the same method (a copy-paste duplicate) are emitted once.
            var seenInMethod = new HashSet<(StepType, string)>();

            foreach (var attrList in method.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var name = GetUnqualifiedName(attr);
                    if (!StepAttributeNames.Contains(name)) continue;

                    var pattern = ExtractPattern(attr);
                    if (pattern is null)
                    {
                        logger.Warn($"[{name}] at {relativePath} has no string argument — skipping.");
                        continue;
                    }

                    var @params = ExtractParams(method.ParameterList, pattern, logger);
                    var declaredColumns = ResolveDeclaredColumns(method, compilationUnit);
                    if (declaredColumns is not null)
                        @params = AttachColumnsToFirstTable(@params, declaredColumns);
                    var line = attr.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var source = method.ToString();

                    if (!Enum.TryParse<StepType>(name, out var stepType))
                    {
                        logger.Warn($"[{name}] at {relativePath} has no matching StepType value — skipping.");
                        continue;
                    }

                    if (!seenInMethod.Add((stepType, pattern)))
                    {
                        logger.Warn(
                            $"Duplicate step attribute at {relativePath}:{line} — " +
                            $"[{name}(\"{pattern}\")] already present on this method; skipping the duplicate.");
                        continue;
                    }

                    steps.Add(new RawStep(stepType, pattern, @params, relativePath, line, source));
                    logger.Verbose($"  [{name}] {pattern} at {relativePath}:{line}");
                }
            }
        }

        logger.Info($"  {relativePath}: {steps.Count} step(s)");
        return steps.AsReadOnly();
    }

    private static string GetUnqualifiedName(AttributeSyntax attr)
    {
        var fullName = attr.Name.ToString();
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
    }

    private static string? ExtractPattern(AttributeSyntax attr)
    {
        if (attr.ArgumentList is null) return null;
        foreach (var arg in attr.ArgumentList.Arguments)
        {
            if (arg.Expression is LiteralExpressionSyntax lit &&
                lit.IsKind(SyntaxKind.StringLiteralExpression))
                return lit.Token.ValueText;
        }
        return null;
    }

    private static IReadOnlyList<ParamRecord> ExtractParams(
        ParameterListSyntax paramList, string pattern, IDocGenLogger logger)
    {
        var placeholders = PlaceholderPattern.Matches(pattern);
        var result = new List<ParamRecord>();
        var placeholderIndex = 0;

        foreach (var param in paramList.Parameters)
        {
            var csType = param.Type?.ToString() ?? ParamTypes.String;
            var name = param.Identifier.Text;
            string schemaType;
            string example;

            switch (csType)
            {
                case "int":
                    schemaType = ParamTypes.Int;
                    example = "0";
                    placeholderIndex++;
                    break;
                case "decimal":
                    schemaType = ParamTypes.Decimal;
                    example = "0.00";
                    placeholderIndex++;
                    break;
                case "string":
                    schemaType = placeholderIndex < placeholders.Count
                        ? ParamTypes.String
                        : ParamTypes.DocString;
                    example = "";
                    placeholderIndex++;
                    break;
                case "Table":
                case "DataTable":
                    schemaType = ParamTypes.Table;
                    example = "";
                    break;
                case "ScenarioContext":
                    // Known Reqnroll/SpecFlow injection type passed by the framework, not bound to a placeholder.
                    schemaType = ParamTypes.String;
                    example = "";
                    break;
                default:
                    logger.Warn($"Unrecognised parameter type '{csType}' on '{name}' — defaulting to string.");
                    schemaType = ParamTypes.String;
                    example = "";
                    placeholderIndex++;
                    break;
            }

            result.Add(new ParamRecord(name, schemaType, example));
        }

        return result.AsReadOnly();
    }

    private static IReadOnlyList<ColumnRecord>? ResolveDeclaredColumns(
        MethodDeclarationSyntax method, CompilationUnitSyntax compilationUnit)
    {
        if (method.Body is null && method.ExpressionBody is null) return null;

        var generics = method.DescendantNodes()
            .OfType<GenericNameSyntax>()
            .Where(g => g.Identifier.Text is "CreateSet" or "CreateInstance"
                     && g.TypeArgumentList.Arguments.Count == 1)
            .ToList();

        foreach (var generic in generics)
        {
            var typeName = generic.TypeArgumentList.Arguments[0].ToString();
            // Strip namespace qualifier; keep the simple name.
            var simpleName = typeName.Contains('.') ? typeName[(typeName.LastIndexOf('.') + 1)..] : typeName;

            var typeDecl = compilationUnit.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault(t => t.Identifier.Text == simpleName);
            if (typeDecl is null) continue;

            var columns = typeDecl.Members
                .OfType<PropertyDeclarationSyntax>()
                .Where(IsPublicOrDefaultAccess)
                .Select(p => new ColumnRecord(p.Identifier.Text, p.Type.ToString()))
                .ToList();
            if (columns.Count > 0) return columns.AsReadOnly();
        }

        return null;
    }

    private static bool IsPublicOrDefaultAccess(PropertyDeclarationSyntax p)
    {
        var mods = p.Modifiers;
        if (mods.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) return true;
        if (mods.Any(m => m.IsKind(SyntaxKind.PrivateKeyword)
                       || m.IsKind(SyntaxKind.ProtectedKeyword)
                       || m.IsKind(SyntaxKind.InternalKeyword))) return false;
        // No explicit accessor → default to public for top-level types (heuristic; good enough for typed table models).
        return true;
    }

    private static IReadOnlyList<ParamRecord> AttachColumnsToFirstTable(
        IReadOnlyList<ParamRecord> @params, IReadOnlyList<ColumnRecord> columns)
    {
        var result = new List<ParamRecord>(@params.Count);
        var attached = false;
        foreach (var p in @params)
        {
            if (!attached && p.Type == ParamTypes.Table)
            {
                result.Add(p with { Columns = columns });
                attached = true;
            }
            else
            {
                result.Add(p);
            }
        }
        return result.AsReadOnly();
    }
}
