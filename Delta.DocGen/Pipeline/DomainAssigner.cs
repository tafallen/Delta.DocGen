using Microsoft.Extensions.FileSystemGlobbing;
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Pipeline;

public static class DomainAssigner
{
    public static IReadOnlyList<RawStep> Assign(
        IReadOnlyList<RawStep> steps,
        IReadOnlyList<DomainRule> rules,
        string fallbackDomain,
        IDocGenLogger logger)
    {
        var matchers = rules
            .Select(r => (Rule: r, Matcher: BuildMatcher(r.Pattern)))
            .ToList();

        var result = new List<RawStep>(steps.Count);
        foreach (var step in steps)
        {
            var matched = false;
            foreach (var (rule, matcher) in matchers)
            {
                if (matcher.Match(step.File).HasMatches)
                {
                    result.Add(step with { Domain = rule.Domain });
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                logger.Warn($"Step in {step.File} matched no domain rule; assigned '{fallbackDomain}'.");
                result.Add(step with { Domain = fallbackDomain });
            }
        }
        logger.Info($"Domain assignment complete: {result.Count} step(s) assigned.");
        return result.AsReadOnly();
    }

    private static Matcher BuildMatcher(string pattern)
    {
        var matcher = new Matcher();
        matcher.AddInclude(pattern);
        return matcher;
    }
}
