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
                    var line = attr.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var source = method.ToString();

                    if (!Enum.TryParse<StepType>(name, out var stepType))
                    {
                        logger.Warn($"[{name}] at {relativePath} has no matching StepType value — skipping.");
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
                case "ScenarioContext":
                    // Known Reqnroll/SpecFlow injection types passed by the framework, not bound to a placeholder.
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
}
