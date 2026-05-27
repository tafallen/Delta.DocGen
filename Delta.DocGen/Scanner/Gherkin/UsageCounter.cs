using System.Text;
using System.Text.RegularExpressions;
using Gherkin;
using Gherkin.Ast;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Scanner.Gherkin;

public static class UsageCounter
{
    private static readonly Regex CucumberPlaceholder =
        new(@"\{(\w+)\}", RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, int> Count(
        IReadOnlyList<RawStep> steps,
        string relativePath,
        string root,
        IDocGenLogger logger)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var text = File.ReadAllText(fullPath, Encoding.UTF8);

        var counts = steps
            .GroupBy(s => s.Pattern, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, _ => 0, StringComparer.Ordinal);

        var regexes = counts.Keys.ToDictionary(
            p => p,
            p => BuildMatchRegex(p),
            StringComparer.Ordinal);

        GherkinDocument doc;
        try
        {
            doc = new Parser().Parse(new StringReader(text));
        }
        catch (ParserException ex)
        {
            logger.Warn($"Could not parse feature file {relativePath}: {ex.Message}");
            return counts;
        }

        if (doc.Feature is { } feature)
        {
            foreach (var child in feature.Children)
            {
                if (child is Scenario scenario)
                    MatchSteps(scenario.Steps, counts, regexes, relativePath, logger);
                else if (child is Background background)
                    MatchSteps(background.Steps, counts, regexes, relativePath, logger);
                else if (child is Rule rule)
                {
                    foreach (var ruleChild in rule.Children)
                    {
                        if (ruleChild is Scenario ruleScenario)
                            MatchSteps(ruleScenario.Steps, counts, regexes, relativePath, logger);
                        else if (ruleChild is Background ruleBackground)
                            MatchSteps(ruleBackground.Steps, counts, regexes, relativePath, logger);
                    }
                }
            }
        }

        logger.Info($"  {relativePath}: feature file processed");
        return counts;
    }

    private static void MatchSteps(
        IEnumerable<Step> steps,
        Dictionary<string, int> counts,
        Dictionary<string, Regex> regexes,
        string relativePath,
        IDocGenLogger logger)
    {
        foreach (var step in steps)
        {
            var matched = false;
            foreach (var (pattern, regex) in regexes)
            {
                if (regex.IsMatch(step.Text))
                {
                    counts[pattern]++;
                    matched = true;
                    break;
                }
            }
            if (!matched)
                logger.Warn($"  {LogPhrases.UnmatchedStep} {relativePath}: \"{step.Text}\"");
        }
    }

    private static Regex BuildMatchRegex(string cucumberPattern)
    {
        // Patterns starting with '^' are old-style regex — used as-is per spec
        if (cucumberPattern.StartsWith('^'))
            return new Regex(cucumberPattern, RegexOptions.Compiled);

        var sb = new StringBuilder("^");
        var lastIndex = 0;
        foreach (Match m in CucumberPlaceholder.Matches(cucumberPattern))
        {
            sb.Append(Regex.Escape(cucumberPattern[lastIndex..m.Index]));
            sb.Append(m.Groups[1].Value switch
            {
                "int"        => @"\d+",
                "decimal"    => @"[\d.]+",
                "float"      => @"[\d.]+",
                "bigdecimal" => @"[\d.]+",
                "string"     => "(?:\"[^\"]*\"|'[^']*')",
                "word"       => @"\S+",
                _            => @".+",
            });
            lastIndex = m.Index + m.Length;
        }
        sb.Append(Regex.Escape(cucumberPattern[lastIndex..]));
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.Compiled);
    }
}
