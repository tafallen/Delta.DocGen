using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Pipeline;
using Delta.DocGen.Tests.Pipeline.Fixtures;
using FluentAssertions;

namespace Delta.DocGen.Tests.Pipeline;

public sealed class PipelineRunnerTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _root;
    private readonly string _output;

    public PipelineRunnerTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _root      = Path.Combine(_workspace, "src");
        _output    = Path.Combine(_workspace, "dist", "step-library.json");
        Directory.CreateDirectory(_root);
        PipelineFixture.WriteFixture(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private DocGenConfig BuildConfig() => new()
    {
        Root           = _root,
        Output         = _output,
        Exclude        = [],
        LogVerbosity   = "normal",
        FallbackDomain = "General",
        Domains        =
        [
            new("Auth/**",  "Auth",  "Auth & Identity"),
            new("Forms/**", "Forms", "Forms & Input"),
        ],
    };

    [Fact]
    public void RunFixtureProducesSuccessResultWithExpectedCounts()
    {
        var result = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance);

        result.Success.Should().BeTrue();
        result.StepCount.Should().Be(3);
        result.DomainCount.Should().Be(2);
        result.CsFileCount.Should().Be(2);
        result.FeatureFileCount.Should().Be(1);
        result.OutputPath.Should().Be(_output);
        result.SchemaPath.Should().NotBeNullOrEmpty();
        result.Digest.Should().MatchRegex("^[0-9a-f]{64}$");
        result.ElapsedMs.Should().BeGreaterOrEqualTo(0);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void RunWritesEnvelopeAndSchemaFilesToDisk()
    {
        PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance);

        File.Exists(_output).Should().BeTrue();
        var schemaPath = Path.Combine(Path.GetDirectoryName(_output)!, "schema", "v1", "step-library.schema.json");
        File.Exists(schemaPath).Should().BeTrue();
    }
}
