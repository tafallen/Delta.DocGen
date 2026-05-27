using Delta.DocGen.Pipeline;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class DiscovererTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public DiscovererTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Touch(string relativePath)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
    }

    [Fact]
    public void FindsCsAndFeatureFiles()
    {
        Touch("Auth/AuthSteps.cs");
        Touch("Features/login.feature");
        Touch("README.md");

        var result = Discoverer.Discover(_root, excludes: []);

        result.CsFiles.Should().ContainSingle(f => f.EndsWith("AuthSteps.cs"));
        result.FeatureFiles.Should().ContainSingle(f => f.EndsWith("login.feature"));
        result.CsFiles.Should().NotContain(f => f.EndsWith(".md"));
    }

    [Fact]
    public void ExcludesMatchingGlobs()
    {
        Touch("Auth/AuthSteps.cs");
        Touch("meta/MetaTests.cs");
        Touch("meta/helper.feature");

        var result = Discoverer.Discover(_root, excludes: ["**/meta/**"]);

        result.CsFiles.Should().ContainSingle(f => f.EndsWith("AuthSteps.cs"));
        result.CsFiles.Should().NotContain(f => f.Contains("meta"));
        result.FeatureFiles.Should().BeEmpty();
    }

    [Fact]
    public void ReturnsRelativePaths()
    {
        Touch("Auth/AuthSteps.cs");

        var result = Discoverer.Discover(_root, excludes: []);

        result.CsFiles.Should().ContainSingle();
        result.CsFiles[0].Should().Be("Auth/AuthSteps.cs");
    }

    [Fact]
    public void EmptyRootReturnsEmptyLists()
    {
        var result = Discoverer.Discover(_root, excludes: []);

        result.CsFiles.Should().BeEmpty();
        result.FeatureFiles.Should().BeEmpty();
    }
}
