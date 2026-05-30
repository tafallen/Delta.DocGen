using System.Security.Cryptography;
using System.Text;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Pipeline;

public static class IdGenerator
{
    public static IReadOnlyList<StepRecord> AssignIds(
        IReadOnlyList<RawStep> steps,
        IReadOnlyDictionary<string, int> usageCounts,
        IReadOnlyDictionary<string, IReadOnlyList<ColumnRecord>> observedColumns,
        IDocGenLogger logger)
    {
        var seenIds = new Dictionary<string, RawStep>(StringComparer.Ordinal);
        var records = new List<StepRecord>(steps.Count);

        foreach (var step in steps)
        {
            var id = BuildId(step.Type, step.Domain, step.Pattern, logger);
            if (seenIds.TryGetValue(id, out var existing))
            {
                if (existing.File == step.File && existing.Line == step.Line)
                {
                    logger.Warn(
                        $"Duplicate step attribute on {step.File}:{step.Line} for pattern '{step.Pattern}' — skipping the duplicate.");
                    continue;
                }
                // Cross-method collision = ambiguous binding in the user's Reqnroll project.
                // The doc tool surfaces this loudly (Error) and skips the duplicate, rather
                // than aborting the whole run — operators see every collision in one pass.
                logger.Error(
                    $"Ambiguous step binding: '{id}' generated for both " +
                    $"'{existing.Pattern}' ({existing.File}:{existing.Line}) and " +
                    $"'{step.Pattern}' ({step.File}:{step.Line}) — skipping the second binding.");
                continue;
            }
            seenIds[id] = step;

            var used = usageCounts.TryGetValue(step.Pattern, out var count) ? count : 0;
            var obs = observedColumns.TryGetValue(step.Pattern, out var o) ? o : null;
            records.Add(new StepRecord(
                Id:          id,
                Type:        step.Type,
                Pattern:     step.Pattern,
                Params:      MergeColumns(step.Params, obs, logger),
                File:        step.File,
                Line:        step.Line,
                Domain:      step.Domain,
                Tags:        [],
                Used:        used,
                Description: "",
                Source:      step.Source,
                SuggestsNext: []));
        }

        logger.Info($"ID generation complete: {records.Count} step(s) processed.");
        return records.AsReadOnly();
    }

    private static IReadOnlyList<ParamRecord> MergeColumns(
        IReadOnlyList<ParamRecord> @params,
        IReadOnlyList<ColumnRecord>? observed,
        IDocGenLogger logger)
    {
        var result = new List<ParamRecord>(@params.Count);
        var observedAttached = false;
        foreach (var p in @params)
        {
            if (p.Type != ParamTypes.Table)
            {
                result.Add(p);
                continue;
            }

            if (p.Columns is { Count: > 0 } declared)
            {
                var existingSource = p.ColumnsSource ?? "declared";
                if (observed is not null && !observedAttached)
                {
                    var declaredNames = new HashSet<string>(declared.Select(c => c.Name), StringComparer.Ordinal);
                    var extra = observed.Where(o => !declaredNames.Contains(o.Name)).ToList();
                    if (extra.Count > 0)
                    {
                        logger.Verbose(
                            $"Observed columns not in declared type: {string.Join(", ", extra.Select(e => e.Name))} — appending as observed-string.");
                        var merged = declared.Concat(extra.Select(e => new ColumnRecord(e.Name, "string"))).ToList();
                        var mergedSource = existingSource + "+feature";
                        result.Add(p with { Columns = merged.AsReadOnly(), ColumnsSource = mergedSource });
                        observedAttached = true;
                        continue;
                    }
                }
                result.Add(p);  // declared/roslyn as-is, source already set
                observedAttached = true;
            }
            else
            {
                if (observed is not null && !observedAttached)
                {
                    result.Add(p with { Columns = observed, ColumnsSource = "feature" });
                    observedAttached = true;
                }
                else
                {
                    result.Add(p);  // no columns available from any source — leave Columns null
                }
            }
        }
        return result.AsReadOnly();
    }

    private static string BuildId(StepType type, string domain, string pattern, IDocGenLogger logger)
        => $"{DomainPrefix(domain, logger)}-{PatternHash(type, pattern)}";

    internal static string DomainPrefix(string domain, IDocGenLogger? logger = null)
    {
        var sb = new StringBuilder();
        foreach (var ch in domain.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_') sb.Append('-');
        }
        var result = sb.ToString().Trim('-');
        if (result.Length == 0)
        {
            logger?.Warn($"Domain '{domain}' produced an empty ID prefix after sanitisation; using 'unknown'.");
            return "unknown";
        }
        return result;
    }

    internal static string PatternHash(StepType type, string pattern)
    {
        var normalised = pattern.Trim().Normalize(System.Text.NormalizationForm.FormC).ToLowerInvariant();
        var input = $"{type}:{normalised}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }
}
