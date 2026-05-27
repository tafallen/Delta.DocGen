using System.Text.Json;

namespace Delta.DocGen.Config;

/// <summary>Values that CLI arguments can override from the config file.</summary>
public sealed record ConfigOverrides
{
    public string? Root { get; init; }
    public string? Output { get; init; }
    public string? LogVerbosity { get; init; }
    public IReadOnlyList<string> AdditionalExcludes { get; init; } = [];
}

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static DocGenConfig Load(string configPath, ConfigOverrides overrides)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"Config file not found: {configPath}", configPath);

        var json = File.ReadAllText(configPath);
        var file = JsonSerializer.Deserialize<ConfigFile>(json, _options)
                   ?? throw new InvalidOperationException("Config file is empty or invalid JSON.");

        var excludes = new List<string>(file.Exclude ?? []);
        excludes.AddRange(overrides.AdditionalExcludes);

        return new DocGenConfig
        {
            Root           = overrides.Root         ?? file.Root         ?? throw new InvalidOperationException("'root' is required in config."),
            Output         = overrides.Output        ?? file.Output       ?? throw new InvalidOperationException("'output' is required in config."),
            LogVerbosity   = overrides.LogVerbosity  ?? file.LogVerbosity ?? "normal",
            FallbackDomain = file.FallbackDomain     ?? "General",
            Exclude        = excludes.AsReadOnly(),
            Domains        = (file.Domains ?? []).Select(d => new DomainRule(d.Pattern, d.Domain, d.Label)).ToList().AsReadOnly(),
        };
    }

    private sealed class ConfigFile
    {
        public string? Root { get; set; }
        public string? Output { get; set; }
        public List<string>? Exclude { get; set; }
        public string? LogVerbosity { get; set; }
        public List<DomainRuleDto>? Domains { get; set; }
        public string? FallbackDomain { get; set; }
    }

    private sealed class DomainRuleDto
    {
        public string Pattern { get; set; } = "";
        public string Domain { get; set; } = "";
        public string Label { get; set; } = "";
    }
}
