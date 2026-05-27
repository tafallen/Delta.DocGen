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
    /// </summary>
    public static DiscoveryResult Discover(string root, IReadOnlyList<string> excludes)
    {
        var matcher = new Matcher();
        matcher.AddInclude("**/*.cs");
        matcher.AddInclude("**/*.feature");
        foreach (var ex in excludes)
            matcher.AddExclude(ex);

        var dir = new DirectoryInfoWrapper(new DirectoryInfo(root));
        var matches = matcher.Execute(dir);

        var csFiles = new List<string>();
        var featureFiles = new List<string>();

        foreach (var match in matches.Files)
        {
            // Normalise to forward slashes regardless of OS
            var relative = match.Path.Replace(Path.DirectorySeparatorChar, '/');
            if (relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                csFiles.Add(relative);
            else if (relative.EndsWith(".feature", StringComparison.OrdinalIgnoreCase))
                featureFiles.Add(relative);
        }

        return new DiscoveryResult(csFiles.AsReadOnly(), featureFiles.AsReadOnly());
    }
}
