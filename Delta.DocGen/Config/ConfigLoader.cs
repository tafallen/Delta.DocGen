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
            Root           = ResolveRequired(overrides.Root,        file.Root,        "root"),
            Output         = ResolveRequired(overrides.Output,      file.Output,      "output"),
            LogVerbosity   = overrides.LogVerbosity ?? file.LogVerbosity ?? "normal",
            FallbackDomain = file.FallbackDomain ?? "General",
            Exclude        = excludes.AsReadOnly(),
            Domains        = MapDomains(file.Domains),
        };
    }

    private static string ResolveRequired(string? cliVal, string? fileVal, string name)
    {
        var v = cliVal ?? fileVal;
        if (string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException($"'{name}' is required and must not be empty in config or CLI arguments.");
        return v;
    }

    private static IReadOnlyList<DomainRule> MapDomains(List<DomainRuleDto>? dtos)
    {
        if (dtos is null) return [];
        return dtos.Select((d, i) =>
        {
            if (string.IsNullOrWhiteSpace(d.Pattern))
                throw new InvalidOperationException($"Domain rule at index {i} must have a non-empty 'pattern'.");
            if (string.IsNullOrWhiteSpace(d.Domain))
                throw new InvalidOperationException($"Domain rule at index {i} must have a non-empty 'domain'.");
            return new DomainRule(d.Pattern, d.Domain, d.Label);
        }).ToList().AsReadOnly();
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
