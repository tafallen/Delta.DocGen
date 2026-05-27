using Delta.DocGen.Config;
using Delta.DocGen.Logging;
using Delta.DocGen.Pipeline;
using Delta.DocGen.Tests.Logging;
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

    [Fact]
    public void DryRunReturnsSuccessButWritesNoFiles()
    {
        var result = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance, dryRun: true);

        result.Success.Should().BeTrue();
        result.StepCount.Should().Be(3);
        result.Digest.Should().MatchRegex("^[0-9a-f]{64}$");
        result.OutputPath.Should().BeNull();
        result.SchemaPath.Should().BeNull();
        File.Exists(_output).Should().BeFalse();
    }

    [Fact]
    public void DryRunStillComputesDigest()
    {
        Func<DateTime> fixedClock = () => new DateTime(2026, 5, 27, 9, 0, 0, DateTimeKind.Utc);
        var dry = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance, dryRun: true, clock: fixedClock);
        var wet = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance, dryRun: false, clock: fixedClock);

        dry.Digest.Should().Be(wet.Digest);
    }

    [Fact]
    public void UnmatchedFeatureStepsAreCountedInResult()
    {
        // Add a feature with a step that no [Given]/[When]/[Then] matches.
        File.WriteAllText(Path.Combine(_root, "Features", "extra.feature"), """
            Feature: Extra
              Scenario: Mystery
                Given I do something nobody coded
            """);

        var result = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance);

        result.UnmatchedStepCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void PipelineCatchesIdCollisionAndReturnsFailedResult()
    {
        // Two [Given]s with identical pattern in the same domain → ID collision.
        File.WriteAllText(Path.Combine(_root, "Auth", "DuplicateSteps.cs"), """
            using Reqnroll;
            namespace Demo;
            public class DuplicateSteps
            {
                [Given("I am logged in")]
                public void GivenDuplicate() { }
            }
            """);

        var logger = new CapturingDocGenLogger();
        var result = PipelineRunner.Run(BuildConfig(), logger);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        result.ErrorMessage.Should().Contain("collision");
        logger.ErrorMessages.Should().ContainSingle(m => m.Contains("collision"));
        File.Exists(_output).Should().BeFalse();
    }

    [Fact]
    public void RunOnNonExistentRootDirectoryReturnsFailedResult()
    {
        var config = BuildConfig() with { Root = Path.Combine(_workspace, "does-not-exist") };

        var result = PipelineRunner.Run(config, NullDocGenLogger.Instance);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RunEmitsSummaryLogWithKeyCounts()
    {
        var logger = new CapturingDocGenLogger();

        PipelineRunner.Run(BuildConfig(), logger);

        logger.SummaryMessages.Should().ContainSingle(m =>
            m.Contains("3 step") && m.Contains("2 domain") && m.Contains("2 C#") && m.Contains("1 feature"));
    }

    [Fact]
    public void UserErrorFromMissingRootMapsToUserErrorCategory()
    {
        var config = BuildConfig() with { Root = Path.Combine(_workspace, "does-not-exist") };

        var result = PipelineRunner.Run(config, NullDocGenLogger.Instance);

        result.Success.Should().BeFalse();
        result.FailureCategory.Should().Be(FailureCategory.UserError);
    }

    [Fact]
    public void InternalErrorFromIdCollisionMapsToInternalErrorCategory()
    {
        File.WriteAllText(Path.Combine(_root, "Auth", "DuplicateSteps.cs"), """
            using Reqnroll;
            namespace Demo;
            public class DuplicateSteps
            {
                [Given("I am logged in")]
                public void GivenDuplicate() { }
            }
            """);

        var result = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance);

        result.Success.Should().BeFalse();
        result.FailureCategory.Should().Be(FailureCategory.InternalError);
    }

    [Fact]
    public void SuccessfulRunHasNoFailureCategory()
    {
        var result = PipelineRunner.Run(BuildConfig(), NullDocGenLogger.Instance);

        result.FailureCategory.Should().Be(FailureCategory.None);
    }

    [Fact]
    public void DryRunEmitsVerboseAboutUnresolvedSchemaReference()
    {
        var logger = new CapturingDocGenLogger();

        PipelineRunner.Run(BuildConfig(), logger, dryRun: true);

        logger.VerboseMessages.Should().ContainSingle(m => m.Contains("$schema") && m.Contains("Dry-run"));
    }
}
