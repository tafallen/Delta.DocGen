using Delta.DocGen.Config;
using Delta.DocGen.Model;

namespace Delta.DocGen.Pipeline;

public static class DomainBuilder
{
    public static IReadOnlyList<DomainRecord> Build(
        IReadOnlyList<RawStep> steps,
        IReadOnlyList<DomainRule> domainRules)
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
