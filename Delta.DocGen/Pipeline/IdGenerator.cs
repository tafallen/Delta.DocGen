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
                throw new InvalidOperationException(
                    $"Step ID collision: '{id}' generated for both " +
                    $"'{existing.Pattern}' ({existing.File}:{existing.Line}) and " +
                    $"'{step.Pattern}' ({step.File}:{step.Line}).");
            }
            seenIds[id] = step;

            var used = usageCounts.TryGetValue(step.Pattern, out var count) ? count : 0;
            records.Add(new StepRecord(
                Id:          id,
                Type:        step.Type,
                Pattern:     step.Pattern,
                Params:      step.Params,
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
