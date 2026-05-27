using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Delta.DocGen.Pipeline;

public sealed record DiscoveryResult(
    IReadOnlyList<string> CsFiles,
    IReadOnlyList<string> FeatureFiles
);

public static class Discoverer
{
    /// <summary>
    /// Walks <paramref name="root"/> and returns relative paths (forward-slash separated)
    /// for all .cs and .feature files not matched by any exclude glob.
    /// Results are sorted ordinally for deterministic output.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">Thrown if <paramref name="root"/> does not exist.</exception>
    public static DiscoveryResult Discover(string root, IReadOnlyList<string>? excludes)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Root directory does not exist: {root}");

        var matcher = new Matcher();
        // Both root-level and recursive patterns are required.
        // FileSystemGlobbing interprets "**/*.cs" as "one or more path segments then *.cs",
        // so it does NOT match files directly in the root directory. "*.cs" catches those.
        matcher.AddInclude("*.cs");
        matcher.AddInclude("*.feature");
        matcher.AddInclude("**/*.cs");
        matcher.AddInclude("**/*.feature");
        foreach (var ex in excludes ?? [])
            matcher.AddExclude(ex);

        var dir = new DirectoryInfoWrapper(new DirectoryInfo(root));
        var matches = matcher.Execute(dir);

        var csFiles = new List<string>();
        var featureFiles = new List<string>();

        foreach (var match in matches.Files)
        {
            // FileSystemGlobbing returns forward-slash paths; replace is a safety net for any future runtime variance
            var relative = match.Path.Replace('\\', '/');
            if (relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                csFiles.Add(relative);
            else if (relative.EndsWith(".feature", StringComparison.OrdinalIgnoreCase))
                featureFiles.Add(relative);
        }

        return new DiscoveryResult(
            csFiles.OrderBy(f => f, StringComparer.Ordinal).ToList().AsReadOnly(),
            featureFiles.OrderBy(f => f, StringComparer.Ordinal).ToList().AsReadOnly()
        );
    }
}
