namespace Delta.DocGen.Config;

public sealed record DocGenConfig
{
    public required string Root { get; init; }
    public required string Output { get; init; }
    public IReadOnlyList<string> Exclude { get; init; } = [];
    public string LogVerbosity { get; init; } = "normal";
    public IReadOnlyList<DomainRule> Domains { get; init; } = [];
    public string FallbackDomain { get; init; } = "General";
}
