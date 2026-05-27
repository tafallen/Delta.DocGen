using Delta.DocGen.Logging;

namespace Delta.DocGen.Config;

public static class ConfigDefaults
{
    public const string FallbackDomain = "General";
}

public sealed record DocGenConfig
{
    public required string Root { get; init; }
    public required string Output { get; init; }
    public IReadOnlyList<string> Exclude { get; init; } = [];
    public string LogVerbosity { get; init; } = Logging.LogVerbosity.Normal;
    public IReadOnlyList<DomainRule> Domains { get; init; } = [];
    public string FallbackDomain { get; init; } = ConfigDefaults.FallbackDomain;
}
