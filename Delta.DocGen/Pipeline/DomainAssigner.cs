using Microsoft.Extensions.FileSystemGlobbing;
using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Model;

namespace Delta.DocGen.Pipeline;

/// <summary>
/// Assigns each <see cref="Model.RawStep"/> a <c>Domain</c> by matching its <c>File</c> path
/// against a list of glob rules. Evaluation is <b>first-match-wins</b> in declaration order;
/// steps matching no rule receive the supplied fallback domain and a Warn.
/// </summary>
public static class DomainAssigner
{
    public static IReadOnlyList<RawStep> Assign(
        IReadOnlyList<RawStep> steps,
        IReadOnlyList<DomainRule> rules,
        string fallbackDomain,
        IDocGenLogger logger)
    {
        if (rules.Count == 0)
        {
            if (steps.Count > 0)
                logger.Warn($"No domain rules configured; assigning fallback domain '{fallbackDomain}' to all {steps.Count} step(s).");
            var fallbackResult = new List<RawStep>(steps.Count);
            foreach (var step in steps)
                fallbackResult.Add(step with { Domain = fallbackDomain });
            logger.Info($"Domain assignment complete: {fallbackResult.Count} step(s) assigned.");
            return fallbackResult.AsReadOnly();
        }

        var matchers = rules
            .Select(r => (Rule: r, Matcher: BuildMatcher(r.Pattern)))
            .ToList();

        var result = new List<RawStep>(steps.Count);
        foreach (var step in steps)
        {
            var matched = false;
            var normalisedFile = step.File.Replace('\\', '/');
            foreach (var (rule, matcher) in matchers)
            {
                if (matcher.Match(normalisedFile).HasMatches)
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
