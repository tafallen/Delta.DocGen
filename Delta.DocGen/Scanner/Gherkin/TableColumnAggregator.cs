using System.Text;
using System.Text.RegularExpressions;
using Gherkin;
using Gherkin.Ast;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Scanner.Gherkin;

public static class TableColumnAggregator
{
    private static readonly Regex CucumberPlaceholder =
        new(@"\{(\w+)\}", RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, IReadOnlyList<ColumnRecord>> Aggregate(
        IReadOnlyList<RawStep> steps,
        IReadOnlyList<string> featureRelativePaths,
        string root,
        IDocGenLogger logger)
    {
        // observations[pattern][columnName] -> all observed raw cell values across uses.
        var observations = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        // headerOrder[pattern] -> ordered column names (first-seen order, union across scenarios).
        var headerOrder  = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var patterns = steps.Select(s => s.Pattern).Distinct(StringComparer.Ordinal).ToList();
        var regexes  = patterns.ToDictionary(p => p, BuildMatchRegex, StringComparer.Ordinal);

        foreach (var featurePath in featureRelativePaths)
        {
            var fullPath = Path.Combine(root, featurePath.Replace('/', Path.DirectorySeparatorChar));
            string text;
            try { text = File.ReadAllText(fullPath, Encoding.UTF8); }
            catch (FileNotFoundException) { continue; }

            GherkinDocument doc;
            try { doc = new Parser().Parse(new StringReader(text)); }
            catch (ParserException) { continue; }

            if (doc.Feature is not { } feature) continue;

            foreach (var child in feature.Children)
            {
                switch (child)
                {
                    case Scenario sc:
                        ObserveSteps(sc.Steps, patterns, regexes, observations, headerOrder);
                        break;
                    case Background bg:
                        ObserveSteps(bg.Steps, patterns, regexes, observations, headerOrder);
                        break;
                    case Rule rule:
                        foreach (var rc in rule.Children)
                        {
                            if (rc is Scenario rs)
                                ObserveSteps(rs.Steps, patterns, regexes, observations, headerOrder);
                            else if (rc is Background rbg)
                                ObserveSteps(rbg.Steps, patterns, regexes, observations, headerOrder);
                        }
                        break;
                }
            }
        }

        var result = new Dictionary<string, IReadOnlyList<ColumnRecord>>(StringComparer.Ordinal);
        foreach (var (pattern, _) in observations)
        {
            var ordered = headerOrder[pattern];
            // Task 3: string-only types. Task 4 will narrow each column based on observed values.
            result[pattern] = ordered.Select(name => new ColumnRecord(name, ParamTypes.String))
                                     .ToList().AsReadOnly();
        }
        logger.Info($"Table column aggregation complete: {result.Count} step pattern(s) with observed columns.");
        return result;
    }

    private static void ObserveSteps(
        IEnumerable<Step> stepsInScenario,
        IReadOnlyList<string> patterns,
        IReadOnlyDictionary<string, Regex> regexes,
        Dictionary<string, Dictionary<string, List<string>>> observations,
        Dictionary<string, List<string>> headerOrder)
    {
        foreach (var step in stepsInScenario)
        {
            if (step.Argument is not DataTable table) continue;
            var rows = table.Rows.ToList();
            if (rows.Count == 0) continue;
            var headers = rows[0].Cells.Select(c => c.Value).ToList();

            foreach (var pattern in patterns)
            {
                if (!regexes[pattern].IsMatch(step.Text)) continue;

                if (!observations.TryGetValue(pattern, out var perColumn))
                {
                    perColumn = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    observations[pattern] = perColumn;
                    headerOrder[pattern] = new List<string>(headers);
                }
                else
                {
                    foreach (var h in headers)
                        if (!headerOrder[pattern].Contains(h, StringComparer.Ordinal))
                            headerOrder[pattern].Add(h);
                }

                for (var rowIdx = 1; rowIdx < rows.Count; rowIdx++)
                {
                    var cells = rows[rowIdx].Cells.ToList();
                    for (var c = 0; c < headers.Count && c < cells.Count; c++)
                    {
                        if (!perColumn.TryGetValue(headers[c], out var values))
                        {
                            values = new List<string>();
                            perColumn[headers[c]] = values;
                        }
                        values.Add(cells[c].Value);
                    }
                }
                break;  // first-match-wins, mirrors UsageCounter
            }
        }
    }

    private static Regex BuildMatchRegex(string pattern)
    {
        if (pattern.StartsWith('^')) return new Regex(pattern, RegexOptions.Compiled);
        var sb = new StringBuilder("^");
        var lastEnd = 0;
        foreach (Match ph in CucumberPlaceholder.Matches(pattern))
        {
            sb.Append(Regex.Escape(pattern[lastEnd..ph.Index]));
            sb.Append(ph.Groups[1].Value switch
            {
                "int"        => @"\d+",
                "decimal"    => @"[\d.]+",
                "float"      => @"[\d.]+",
                "bigdecimal" => @"[\d.]+",
                "string"     => "(?:\"[^\"]*\"|'[^']*')",
                "word"       => @"\S+",
                _            => @".+",
            });
            lastEnd = ph.Index + ph.Length;
        }
        sb.Append(Regex.Escape(pattern[lastEnd..]));
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.Compiled);
    }
}
