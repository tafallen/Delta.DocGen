using System.Security.Cryptography;
using System.Text;
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Pipeline;

public static class IdGenerator
{
    public static (IReadOnlyList<StepRecord> Steps, IReadOnlyList<DomainRecord> Domains) Generate(
        IReadOnlyList<RawStep> steps,
        IReadOnlyDictionary<string, int> usageCounts,
        IReadOnlyList<DomainRule> domainRules,
        string fallbackDomain,
        IDocGenLogger logger)
    {
        var seenIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var records = new List<StepRecord>(steps.Count);

        foreach (var step in steps)
        {
            var id = BuildId(step.Domain, step.Pattern);
            if (seenIds.TryGetValue(id, out var existingPattern))
                throw new InvalidOperationException(
                    $"Step ID collision: '{id}' generated for both '{existingPattern}' and '{step.Pattern}'.");
            seenIds[id] = step.Pattern;

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
        var domains = BuildDomains(steps, domainRules, fallbackDomain);
        return (records.AsReadOnly(), domains);
    }

    private static string BuildId(string domain, string pattern)
        => $"{DomainPrefix(domain)}-{PatternHash(pattern)}";

    internal static string DomainPrefix(string domain)
    {
        var sb = new StringBuilder();
        foreach (var ch in domain.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_') sb.Append('-');
        }
        var result = sb.ToString().Trim('-');
        return result.Length > 0 ? result : "unknown";
    }

    internal static string PatternHash(string pattern)
    {
        var normalized = pattern.Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    private static IReadOnlyList<DomainRecord> BuildDomains(
        IReadOnlyList<RawStep> steps,
        IReadOnlyList<DomainRule> domainRules,
        string fallbackDomain)
    {
        var labelLookup = domainRules
            .GroupBy(r => r.Domain, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.Ordinal);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<DomainRecord>();
        foreach (var step in steps)
        {
            if (!seen.Add(step.Domain)) continue;
            var label = labelLookup.TryGetValue(step.Domain, out var l) ? l : step.Domain;
            result.Add(new DomainRecord(step.Domain, label));
        }
        return result.AsReadOnly();
    }
}
