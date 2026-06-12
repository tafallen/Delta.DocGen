using Delta.DocGen.Logging;
using Delta.DocGen.Pipeline;
using Delta.DocGen.Tests.Logging;
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

        var result = Discoverer.Discover(_root, excludes: [], NullDocGenLogger.Instance);

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

        var result = Discoverer.Discover(_root, excludes: ["**/meta/**"], NullDocGenLogger.Instance);

        result.CsFiles.Should().ContainSingle(f => f.EndsWith("AuthSteps.cs"));
        result.CsFiles.Should().NotContain(f => f.Contains("meta"));
        result.FeatureFiles.Should().BeEmpty();
    }

    [Fact]
    public void ReturnsRelativePaths()
    {
        Touch("Auth/AuthSteps.cs");

        var result = Discoverer.Discover(_root, excludes: [], NullDocGenLogger.Instance);

        result.CsFiles.Should().ContainSingle();
        result.CsFiles[0].Should().Be("Auth/AuthSteps.cs");
    }

    [Fact]
    public void EmptyRootReturnsEmptyLists()
    {
        var result = Discoverer.Discover(_root, excludes: [], NullDocGenLogger.Instance);

        result.CsFiles.Should().BeEmpty();
        result.FeatureFiles.Should().BeEmpty();
    }

    [Fact]
    public void ThrowsIfRootDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "root");
        var act = () => Discoverer.Discover(missing, excludes: [], NullDocGenLogger.Instance);
        act.Should().Throw<DirectoryNotFoundException>().WithMessage("*does not exist*");
    }

    [Fact]
    public void FindsFilesAtRootLevel()
    {
        Touch("RootSteps.cs");
        Touch("root.feature");

        var result = Discoverer.Discover(_root, excludes: [], NullDocGenLogger.Instance);

        result.CsFiles.Should().ContainSingle(f => f == "RootSteps.cs");
        result.FeatureFiles.Should().ContainSingle(f => f == "root.feature");
    }

    [Fact]
    public void ResultsAreSortedOrdinally()
    {
        Touch("Z/ZSteps.cs");
        Touch("A/ASteps.cs");
        Touch("M/MSteps.cs");

        var result = Discoverer.Discover(_root, excludes: [], NullDocGenLogger.Instance);

        result.CsFiles.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void EmptyExcludesListFindsAllFiles()
    {
        Touch("Auth/AuthSteps.cs");

        var result = Discoverer.Discover(_root, excludes: [], NullDocGenLogger.Instance);

        result.CsFiles.Should().ContainSingle();
    }

    [Fact]
    public void ExcludeGlobMatchingNothingIsHarmless()
    {
        Touch("Auth/AuthSteps.cs");

        var result = Discoverer.Discover(_root, excludes: ["**/nonexistent/**"], NullDocGenLogger.Instance);

        result.CsFiles.Should().ContainSingle();
    }

    [Fact]
    public void DiscoverLogsCompletionWithFileCounts()
    {
        var logger = new CapturingDocGenLogger();
        File.WriteAllText(Path.Combine(_root, "Steps.cs"), "");
        File.WriteAllText(Path.Combine(_root, "feature.feature"), "Feature: Sample");

        Discoverer.Discover(_root, [], logger);

        logger.InfoMessages.Should().ContainSingle(m =>
            m.Contains("Discovery complete") && m.Contains("1 C#") && m.Contains("1 feature"));
    }
}
